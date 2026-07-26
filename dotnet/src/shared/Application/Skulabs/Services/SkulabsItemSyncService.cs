using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Skulabs.Services;

/// <summary>
/// Reconciles the local SkuLabs item table with the SkuLabs API by considering only the
/// link identifiers — Shopify variant ID on one side, SkuLabs source item ID on the other.
/// Field-level metadata (title, sku, barcode, listing id) is written only when a link is
/// created or re-linked. When the link identifiers already match what the API reports the
/// row is left untouched.
/// </summary>
public class SkulabsItemSyncService(
    ISkulabsItemClient skulabsClient,
    ApplicationDbContext dbContext,
    ILogger<SkulabsItemSyncService> logger) : ISkulabsItemSyncService
{
    public async Task<SkulabsItemSyncResult> Sync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Starting SkuLabs item sync.");

        var collection = await skulabsClient.GetAllItems();
        if (collection.Items.Count == 0)
        {
            // An empty response is treated as "nothing to do" rather than "everything is gone", so a
            // transient empty payload never wipes the active links or the ambiguous quarantine.
            logger.LogInformation("SkuLabs returned no items. Sync finished with nothing to do.");
            return SkulabsItemSyncResult.Empty;
        }

        var syncable = collection.GetSyncable();
        var ambiguous = collection.GetAmbiguous();
        logger.LogDebug(
            "Reconciler received {Syncable} syncable and {Ambiguous} ambiguous item(s) from the SkuLabs client.",
            syncable.Count, ambiguous.Count);

        var variantLookup = await LoadVariantLookupAsync(cancellationToken);
        var indexes = await LoadExistingItemIndexesAsync(cancellationToken);
        logger.LogDebug(
            "Loaded {VariantCount} Shopify variant(s) and {ExistingItemCount} existing SkuLabs item(s) from the database.",
            variantLookup.Count, indexes.Count);

        var accumulator = new ReconciliationAccumulator();
        foreach (var apiItem in syncable)
        {
            ReconcileItem(apiItem, variantLookup, indexes, accumulator);
        }

        await ReconcileAmbiguousItems(ambiguous, variantLookup, indexes, accumulator, cancellationToken);

        SeverLinksForItemsWithoutShopifyListings(
            collection.GetSourceItemIdsWithoutShopifyListings(), indexes, accumulator);

        logger.LogDebug(
            "Reconciliation done. About to persist — Created: {Created}, Re-linked: {Relinked}, Severed: {Severed}, "
            + "Unmatched: {Unmatched}, Skipped: {Skipped}, Ambiguous +{AmbCreated}/~{AmbUpdated}/-{AmbRemoved}.",
            accumulator.Created.Count, accumulator.Updated.Count, accumulator.Severed,
            accumulator.Unmatched, accumulator.Skipped,
            accumulator.AmbiguousCreated, accumulator.AmbiguousUpdated, accumulator.AmbiguousRemoved);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Re-throw — the job catches at the top — but attach the planned change counts so
            // the eventual error log explains *what* was being saved when it failed.
            logger.LogError(exception,
                "SaveChanges failed mid-sync. Planned writes — Created: {Created}, Re-linked: {Relinked}, Severed: {Severed}.",
                accumulator.Created.Count, accumulator.Updated.Count, accumulator.Severed);
            throw;
        }

        logger.LogInformation(
            "SkuLabs item sync finished. Created: {Created}, Re-linked: {Relinked}, Severed: {Severed}, "
            + "Unmatched: {Unmatched}, Skipped: {Skipped}, Ambiguous +{AmbCreated}/~{AmbUpdated}/-{AmbRemoved}.",
            accumulator.Created.Count, accumulator.Updated.Count, accumulator.Severed,
            accumulator.Unmatched, accumulator.Skipped,
            accumulator.AmbiguousCreated, accumulator.AmbiguousUpdated, accumulator.AmbiguousRemoved);

        return accumulator.ToResult();
    }

    /// <summary>
    /// Loads a lookup from Shopify's numeric variant ID to the local database Guid. Only the
    /// two columns are projected so this stays cheap even with thousands of variants.
    /// </summary>
    private Task<Dictionary<long, Guid>> LoadVariantLookupAsync(CancellationToken cancellationToken) =>
        dbContext.ShopifyProductVariants
            .Select(v => new { v.VariantId, v.ShopifyProductVariantId })
            .ToDictionaryAsync(v => v.VariantId, v => v.ShopifyProductVariantId, cancellationToken);

    /// <summary>
    /// Loads every existing SkuLabs item once and builds two indexes over the same tracked
    /// entity instances — one by SkuLabs source item ID, one by variant Guid. Both indexes
    /// must stay in sync as the reconciler mutates state.
    /// </summary>
    private async Task<SkulabsItemIndexes> LoadExistingItemIndexesAsync(CancellationToken cancellationToken)
    {
        var existing = await dbContext.SkulabsItems.ToListAsync(cancellationToken);
        var indexes = new SkulabsItemIndexes();
        foreach (var entity in existing)
        {
            indexes.Add(entity);
        }
        return indexes;
    }

    /// <summary>
    /// Processes a single SkuLabs API item: routes it to no-op / re-link / replace / create
    /// based solely on the (variant, SkuLabs item) link identifiers. Exceptions on one item
    /// never abort the batch.
    /// </summary>
    private void ReconcileItem(
        SkuLabsItem apiItem,
        IReadOnlyDictionary<long, Guid> variantLookup,
        SkulabsItemIndexes indexes,
        ReconciliationAccumulator accumulator)
    {
        try
        {
            if (!variantLookup.TryGetValue(apiItem.ShopifyVariantId, out var variantGuid))
            {
                logger.LogDebug(
                    "SkuLabs item {SkulabsItemId} references Shopify variant ID {VariantId} which is not in the database. Skipping.",
                    apiItem.SkulabsItemId, apiItem.ShopifyVariantId);
                accumulator.Unmatched++;
                return;
            }

            var bySkulabsId = indexes.TryGetByItemId(apiItem.SkulabsItemId);
            var byVariant = indexes.TryGetByVariantGuid(variantGuid);

            // Case A: the link the API reports already exists in the DB exactly as-is.
            // Per the contract, metadata is not refreshed on no-ops.
            if (bySkulabsId is not null && ReferenceEquals(bySkulabsId, byVariant))
            {
                return;
            }

            // Case B: the SkuLabs item exists in the DB but on a different variant — re-link.
            if (bySkulabsId is not null)
            {
                // If the destination variant already holds a *different* SkuLabs item, that
                // row would collide with our re-link on the unique index. Sever it first.
                if (byVariant is not null)
                {
                    SeverLink(byVariant, indexes, accumulator);
                }

                ReLink(bySkulabsId, apiItem, variantGuid, indexes, accumulator);
                return;
            }

            // Case C: brand-new SkuLabs item ID. If the destination variant already has a
            // different SkuLabs item, sever it; then create.
            if (byVariant is not null)
            {
                SeverLink(byVariant, indexes, accumulator);
            }

            CreateLink(apiItem, variantGuid, indexes, accumulator);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to reconcile SkuLabs item {SkulabsItemId}. Continuing with remaining items.",
                apiItem.SkulabsItemId);
            accumulator.Skipped++;
        }
    }

    /// <summary>
    /// Inserts a new SkuLabs item row and emits a "linked" log on the variant.
    /// Metadata fields are seeded from the API payload because this is a new link.
    /// </summary>
    private void CreateLink(
        SkuLabsItem apiItem,
        Guid variantGuid,
        SkulabsItemIndexes indexes,
        ReconciliationAccumulator accumulator)
    {
        var entity = new SkulabsItemEntity
        {
            ShopifyProductVariantId = variantGuid,
            SkulabsSourceItemId = apiItem.SkulabsItemId,
            SkulabsSourceListingId = apiItem.SkulabsListingId,
            Title = apiItem.Title,
            Sku = apiItem.Sku,
            Barcode = apiItem.Barcode
        };
        dbContext.SkulabsItems.Add(entity);
        indexes.Add(entity);
        AddVariantLog(variantGuid, VariantLogMessages.SkulabsLinked(apiItem.SkulabsItemId));
        accumulator.Created.Add(entity.SkulabsItemId);
        logger.LogDebug(
            "Creating link: variant {VariantGuid} ↔ SkuLabs item {SkulabsItemId}.",
            variantGuid, apiItem.SkulabsItemId);
    }

    /// <summary>
    /// Re-points an existing SkuLabs row to a new variant and refreshes its metadata fields
    /// from the API payload. Emits an "unlinked" log on the original variant and a "linked"
    /// log on the new one.
    /// </summary>
    private void ReLink(
        SkulabsItemEntity entity,
        SkuLabsItem apiItem,
        Guid newVariantGuid,
        SkulabsItemIndexes indexes,
        ReconciliationAccumulator accumulator)
    {
        var oldVariantGuid = entity.ShopifyProductVariantId;
        indexes.Repoint(entity, newVariantGuid);

        // Per the contract: metadata is refreshed whenever a (new) link is written.
        entity.ShopifyProductVariantId = newVariantGuid;
        entity.SkulabsSourceListingId = apiItem.SkulabsListingId;
        entity.Title = apiItem.Title;
        entity.Sku = apiItem.Sku;
        entity.Barcode = apiItem.Barcode;

        AddVariantLog(oldVariantGuid, VariantLogMessages.SkulabsUnlinked(apiItem.SkulabsItemId));
        AddVariantLog(newVariantGuid, VariantLogMessages.SkulabsLinked(apiItem.SkulabsItemId));
        accumulator.Updated.Add(entity.SkulabsItemId);
        logger.LogDebug(
            "Re-linking SkuLabs item {SkulabsItemId}: variant {OldVariantGuid} → {NewVariantGuid}.",
            apiItem.SkulabsItemId, oldVariantGuid, newVariantGuid);
    }

    /// <summary>
    /// Deletes a SkuLabs item row that's about to be displaced by a new link. Emits an
    /// "unlinked" log on the variant that's losing the link.
    /// </summary>
    private void SeverLink(
        SkulabsItemEntity entity,
        SkulabsItemIndexes indexes,
        ReconciliationAccumulator accumulator)
    {
        var variantGuid = entity.ShopifyProductVariantId;
        var skulabsItemId = entity.SkulabsSourceItemId;
        dbContext.SkulabsItems.Remove(entity);
        indexes.Remove(entity);
        AddVariantLog(variantGuid, VariantLogMessages.SkulabsUnlinked(skulabsItemId));
        accumulator.Severed++;
        logger.LogDebug(
            "Severing link: variant {VariantGuid} ↮ SkuLabs item {SkulabsItemId}.",
            variantGuid, skulabsItemId);
    }

    private void AddVariantLog(Guid variantGuid, string message)
    {
        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = variantGuid,
            Message = message
        });
    }

    /// <summary>
    /// Reconciles the ambiguous-item quarantine against the current SkuLabs payload: upserts every
    /// still-ambiguous item (refreshing its listings), removes rows for items that are no longer
    /// ambiguous, and severs any active link for an item that has just become ambiguous so a single
    /// SkuLabs item is never both synced and quarantined.
    /// </summary>
    private async Task ReconcileAmbiguousItems(
        IReadOnlyList<SkulabsAmbiguousItem> ambiguous,
        IReadOnlyDictionary<long, Guid> variantLookup,
        SkulabsItemIndexes activeIndexes,
        ReconciliationAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SkulabsAmbiguousItems
            .Include(item => item.Listings)
            .ToListAsync(cancellationToken);
        var existingBySourceId = existing.ToDictionary(item => item.SkulabsSourceItemId, StringComparer.Ordinal);
        var seenSourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var apiItem in ambiguous)
        {
            seenSourceIds.Add(apiItem.SourceItemId);

            // An item that has just become ambiguous may still carry a stale active link from a
            // previous run — sever it so the quarantine and the active table stay mutually exclusive.
            var activeRow = activeIndexes.TryGetByItemId(apiItem.SourceItemId);
            if (activeRow is not null)
            {
                SeverLink(activeRow, activeIndexes, accumulator);
            }

            if (existingBySourceId.TryGetValue(apiItem.SourceItemId, out var entity))
            {
                UpdateAmbiguousItem(entity, apiItem, variantLookup);
                accumulator.AmbiguousUpdated++;
            }
            else
            {
                dbContext.SkulabsAmbiguousItems.Add(CreateAmbiguousItem(apiItem, variantLookup));
                accumulator.AmbiguousCreated++;
            }
        }

        // Items no longer reported as ambiguous leave quarantine. If they are now cleanly syncable,
        // the active pass above has already (re)created their link in this same run.
        foreach (var entity in existing)
        {
            if (!seenSourceIds.Contains(entity.SkulabsSourceItemId))
            {
                dbContext.SkulabsAmbiguousItems.Remove(entity);
                accumulator.AmbiguousRemoved++;
            }
        }
    }

    /// <summary>
    /// Severs the active link for any item that no longer has a Shopify listing. Without this a row
    /// that was synced before its last Shopify listing disappeared would keep a stale link and still
    /// show as in-sync on the item-sync page. Any ambiguous quarantine row for the same item is
    /// removed by <see cref="ReconcileAmbiguousItems"/>, which drops rows it no longer sees.
    /// </summary>
    private void SeverLinksForItemsWithoutShopifyListings(
        IReadOnlyList<string> sourceItemIds,
        SkulabsItemIndexes indexes,
        ReconciliationAccumulator accumulator)
    {
        foreach (var sourceItemId in sourceItemIds)
        {
            var activeRow = indexes.TryGetByItemId(sourceItemId);
            if (activeRow is not null)
            {
                SeverLink(activeRow, indexes, accumulator);
            }
        }
    }

    private static SkulabsAmbiguousItemEntity CreateAmbiguousItem(
        SkulabsAmbiguousItem apiItem,
        IReadOnlyDictionary<long, Guid> variantLookup)
    {
        var entity = new SkulabsAmbiguousItemEntity
        {
            SkulabsSourceItemId = apiItem.SourceItemId,
            Name = apiItem.Name,
            Sku = apiItem.Sku,
            Upc = apiItem.Upc,
            ListingCount = apiItem.Listings.Count
        };

        foreach (var listing in apiItem.Listings)
        {
            entity.Listings.Add(BuildListing(listing, variantLookup));
        }

        return entity;
    }

    /// <summary>
    /// Refreshes a quarantined item's metadata and replaces its listings wholesale — listings can
    /// change between runs and diffing them earns nothing at this size. <c>FirstSeenUtc</c> is left
    /// untouched so the original quarantine time survives.
    /// </summary>
    private void UpdateAmbiguousItem(
        SkulabsAmbiguousItemEntity entity,
        SkulabsAmbiguousItem apiItem,
        IReadOnlyDictionary<long, Guid> variantLookup)
    {
        entity.Name = apiItem.Name;
        entity.Sku = apiItem.Sku;
        entity.Upc = apiItem.Upc;
        entity.ListingCount = apiItem.Listings.Count;
        entity.LastSeenUtc = DateTime.UtcNow;

        dbContext.SkulabsAmbiguousItemListings.RemoveRange(entity.Listings);
        entity.Listings.Clear();
        foreach (var listing in apiItem.Listings)
        {
            entity.Listings.Add(BuildListing(listing, variantLookup));
        }
    }

    private static SkulabsAmbiguousItemListingEntity BuildListing(
        SkulabsApiListing listing,
        IReadOnlyDictionary<long, Guid> variantLookup)
    {
        Guid? variantGuid = null;
        if (long.TryParse(listing.RawVariantId, out var variantId) &&
            variantLookup.TryGetValue(variantId, out var guid))
        {
            variantGuid = guid;
        }

        return new SkulabsAmbiguousItemListingEntity
        {
            SkulabsSourceListingId = listing.ListingId,
            RawVariantId = listing.RawVariantId,
            ShopifyProductId = listing.ShopifyProductId,
            ShopifyProductVariantId = variantGuid
        };
    }

    /// <summary>
    /// Tally of reconciliation outcomes for a single <see cref="Sync"/> run.
    /// </summary>
    private sealed class ReconciliationAccumulator
    {
        public List<Guid> Created { get; } = [];
        public List<Guid> Updated { get; } = [];
        public int Unmatched { get; set; }
        public int Skipped { get; set; }
        public int Severed { get; set; }
        public int AmbiguousCreated { get; set; }
        public int AmbiguousUpdated { get; set; }
        public int AmbiguousRemoved { get; set; }

        public SkulabsItemSyncResult ToResult() => new(
            Created, Updated, Unmatched, Skipped,
            AmbiguousCreated, AmbiguousUpdated, AmbiguousRemoved);
    }

    /// <summary>
    /// Two-way index over the loaded SkuLabs item entities. Both maps reference the same
    /// tracked instances; <see cref="Repoint"/> keeps them coherent across re-link operations.
    /// </summary>
    private sealed class SkulabsItemIndexes
    {
        private readonly Dictionary<string, SkulabsItemEntity> _byItemId = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, SkulabsItemEntity> _byVariantGuid = new();

        /// <summary>Number of distinct SkuLabs items currently indexed.</summary>
        public int Count => _byItemId.Count;

        public void Add(SkulabsItemEntity entity)
        {
            _byItemId[entity.SkulabsSourceItemId] = entity;
            _byVariantGuid[entity.ShopifyProductVariantId] = entity;
        }

        public void Remove(SkulabsItemEntity entity)
        {
            _byItemId.Remove(entity.SkulabsSourceItemId);
            _byVariantGuid.Remove(entity.ShopifyProductVariantId);
        }

        /// <summary>
        /// Updates the variant-side index to reflect a re-link. The caller is responsible for
        /// also assigning the new variant Guid on the entity itself; this method only fixes
        /// the in-memory lookup so subsequent items in the same batch see a consistent view.
        /// </summary>
        public void Repoint(SkulabsItemEntity entity, Guid newVariantGuid)
        {
            _byVariantGuid.Remove(entity.ShopifyProductVariantId);
            _byVariantGuid[newVariantGuid] = entity;
        }

        public SkulabsItemEntity? TryGetByItemId(string skulabsItemId) =>
            _byItemId.GetValueOrDefault(skulabsItemId);

        public SkulabsItemEntity? TryGetByVariantGuid(Guid variantGuid) =>
            _byVariantGuid.GetValueOrDefault(variantGuid);
    }
}
