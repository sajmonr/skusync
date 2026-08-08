using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Skulabs.Services;

/// <summary>
/// Mirrors the SkuLabs item catalogue into the local database: one row per SkuLabs item, plus one row
/// per Shopify listing SkuLabs reports for it. Ambiguity is not a state this service assigns — it
/// falls out of how many listings an item ends up with, so an item that gains or loses a listing needs
/// no migration between tables and keeps its identity.
/// <para>
/// Field-level metadata (title, sku, barcode) is written only when a link is created or the item's
/// resolved variant changes. When the link is unchanged the row's metadata is left alone, because the
/// title is ours to push to SkuLabs and refreshing it here would clobber a local correction that has
/// not been dispatched yet.
/// </para>
/// <para>
/// The warehouse location is the exception, because it flows the other way: SkuLabs owns it and we
/// never push it, so it is refreshed on every run regardless of the link, and a change to it never
/// marks the item pending.
/// </para>
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
            // transient empty payload never wipes the catalogue.
            logger.LogInformation("SkuLabs returned no items. Sync finished with nothing to do.");
            return SkulabsItemSyncResult.Empty;
        }

        var variantLookup = await LoadVariantLookup(cancellationToken);
        var existing = await LoadExistingItems(cancellationToken);
        logger.LogDebug(
            "Loaded {VariantCount} Shopify variant(s) and {ExistingItemCount} existing SkuLabs item(s) from the database.",
            variantLookup.Count, existing.Count);

        var accumulator = new ReconciliationAccumulator();
        foreach (var apiItem in collection.Items)
        {
            UpsertItem(apiItem, variantLookup, existing, accumulator);
        }

        RemoveItemsNoLongerReported(collection, existing, accumulator);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Re-throw — the job catches at the top — but attach the planned change counts so
            // the eventual error log explains *what* was being saved when it failed.
            logger.LogError(exception,
                "SaveChanges failed mid-sync. Planned writes — Created: {Created}, Re-linked: {Relinked}, Removed: {Removed}.",
                accumulator.Created.Count, accumulator.Updated.Count, accumulator.Removed);
            throw;
        }

        logger.LogInformation(
            "SkuLabs item sync finished. Created: {Created}, Re-linked: {Relinked}, Removed: {Removed}, "
            + "Unresolved listings: {Unresolved}, Skipped: {Skipped}, Ambiguous: {Ambiguous}.",
            accumulator.Created.Count, accumulator.Updated.Count, accumulator.Removed,
            accumulator.UnresolvedListings, accumulator.Skipped, accumulator.Ambiguous);

        return accumulator.ToResult();
    }

    /// <summary>
    /// Loads a lookup from Shopify's numeric variant ID to the local database Guid. Only the
    /// two columns are projected so this stays cheap even with thousands of variants.
    /// </summary>
    private Task<Dictionary<long, Guid>> LoadVariantLookup(CancellationToken cancellationToken) =>
        dbContext.ShopifyProductVariants
            .Select(v => new { v.VariantId, v.ShopifyProductVariantId })
            .ToDictionaryAsync(v => v.VariantId, v => v.ShopifyProductVariantId, cancellationToken);

    private async Task<Dictionary<string, SkulabsItemEntity>> LoadExistingItems(
        CancellationToken cancellationToken)
    {
        var items = await dbContext.SkulabsItems
            .Include(item => item.Listings)
            .ToListAsync(cancellationToken);

        return items.ToDictionary(item => item.SkulabsSourceItemId, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates or refreshes one SkuLabs item and its listings. Exceptions on one item never abort
    /// the batch.
    /// </summary>
    private void UpsertItem(
        SkulabsApiItem apiItem,
        IReadOnlyDictionary<long, Guid> variantLookup,
        IReadOnlyDictionary<string, SkulabsItemEntity> existing,
        ReconciliationAccumulator accumulator)
    {
        try
        {
            if (!existing.TryGetValue(apiItem.SourceItemId, out var entity))
            {
                CreateItem(apiItem, variantLookup, accumulator);
                return;
            }

            var previousVariantId = ResolvedVariantId(entity.Listings);
            var previousListingCount = entity.Listings.Count;
            entity.LastSeenUtc = DateTime.UtcNow;
            RefreshLocation(entity, apiItem);
            SyncListings(entity, apiItem, variantLookup, accumulator);
            var currentVariantId = ResolvedVariantId(entity.Listings);
            LogLinkTransition(apiItem.SourceItemId, previousVariantId, currentVariantId, previousListingCount, entity);

            RefreshMetadata(entity, apiItem, accumulator);

            if (currentVariantId is not null && currentVariantId != previousVariantId)
            {
                logger.LogDebug(
                    "Re-linked SkuLabs item {SkulabsItemId}: variant {OldVariantGuid} → {NewVariantGuid}.",
                    apiItem.SourceItemId, previousVariantId, currentVariantId);
            }

            accumulator.CountCardinality(entity.Listings.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to reconcile SkuLabs item {SkulabsItemId}. Continuing with remaining items.",
                apiItem.SourceItemId);
            accumulator.Skipped++;
        }
    }

    private void CreateItem(
        SkulabsApiItem apiItem,
        IReadOnlyDictionary<long, Guid> variantLookup,
        ReconciliationAccumulator accumulator)
    {
        var entity = new SkulabsItemEntity
        {
            SkulabsSourceItemId = apiItem.SourceItemId,
            Title = apiItem.Name,
            Sku = apiItem.Sku,
            Barcode = apiItem.Upc,
            Location = apiItem.Location ?? ""
        };

        foreach (var listing in apiItem.Listings)
        {
            entity.Listings.Add(BuildListing(listing, variantLookup, accumulator));
        }

        LogLinkTransition(
            apiItem.SourceItemId,
            previousVariantId: null,
            ResolvedVariantId(entity.Listings),
            previousListingCount: 0,
            entity);

        dbContext.SkulabsItems.Add(entity);
        accumulator.Created.Add(entity.SkulabsItemId);
        accumulator.CountCardinality(entity.Listings.Count);
        logger.LogDebug(
            "Creating SkuLabs item {SkulabsItemId} with {ListingCount} Shopify listing(s).",
            apiItem.SourceItemId, entity.Listings.Count);
    }

    /// <summary>
    /// Mirrors the warehouse location from the payload. Unconditional by design: merchants move bins,
    /// so a location frozen at first-link would be worse than no location at all. Nothing is marked
    /// pending — <see cref="SkulabsItemEntity.PendingSkulabsSync"/> means "we owe SkuLabs a push", and
    /// a location delta means the opposite.
    /// <para>
    /// A null location means no warehouse is configured, so the payload carries no opinion and what
    /// we already hold stands. Turning the feature off must not erase locations it previously synced.
    /// </para>
    /// </summary>
    /// <summary>
    /// Copies the item's fields from the payload, unconditionally.
    /// <para>
    /// This used to be conditional — seeded on a new link, refreshed when the link moved, never
    /// otherwise — because the row doubled as the value we intended to push, and refreshing it would
    /// have destroyed a correction not yet dispatched. Now that decisions live in the desired state,
    /// the row is only a record of what SkuLabs last said, and keeping it stale would have the
    /// reconciler comparing against yesterday's answer.
    /// </para>
    /// </summary>
    private void RefreshMetadata(
        SkulabsItemEntity entity,
        SkulabsApiItem apiItem,
        ReconciliationAccumulator accumulator)
    {
        WarnIfCodeWasCleared(entity, apiItem);

        if (string.Equals(entity.Title, apiItem.Name, StringComparison.Ordinal)
            && string.Equals(entity.Sku, apiItem.Sku, StringComparison.Ordinal)
            && string.Equals(entity.Barcode, apiItem.Upc, StringComparison.Ordinal))
        {
            return;
        }

        entity.Title = apiItem.Name;
        entity.Sku = apiItem.Sku;
        entity.Barcode = apiItem.Upc;
        accumulator.Updated.Add(entity.SkulabsItemId);
    }

    /// <summary>
    /// Reports a code disappearing from an item that previously had one.
    /// <para>
    /// Worth its own warning because nothing else would ever mention it. A blank SkuLabs code is
    /// treated as "none on record" rather than as an instruction, so it propagates nowhere and
    /// produces no drift, no pending row and no audit event — the system stays perfectly quiet while
    /// stock on a shelf carries a label whose code we no longer hold. The likeliest cause is a write
    /// of ours clearing a field we omitted, which is exactly the failure mode we cannot otherwise
    /// see, and the warehouse would find out before we did.
    /// </para>
    /// </summary>
    private void WarnIfCodeWasCleared(SkulabsItemEntity entity, SkulabsApiItem apiItem)
    {
        if (entity.Sku.Length > 0 && apiItem.Sku.Length == 0)
        {
            logger.LogWarning(
                "SkuLabs item {SkulabsItemId} no longer reports a SKU; it previously held '{PreviousSku}'. "
                + "Blank values are never authoritative, so this will not propagate — but stock may be "
                + "labelled with the old code.",
                apiItem.SourceItemId, entity.Sku);
        }

        if (entity.Barcode.Length > 0 && apiItem.Upc.Length == 0)
        {
            logger.LogWarning(
                "SkuLabs item {SkulabsItemId} no longer reports a barcode; it previously held "
                + "'{PreviousBarcode}'. Blank values are never authoritative, so this will not "
                + "propagate — but stock may be labelled with the old code.",
                apiItem.SourceItemId, entity.Barcode);
        }
    }

    private void RefreshLocation(SkulabsItemEntity entity, SkulabsApiItem apiItem)
    {
        if (apiItem.Location is null || entity.Location == apiItem.Location)
        {
            return;
        }

        logger.LogInformation(
            "SkuLabs item {SkulabsItemId} moved location: '{OldLocation}' → '{NewLocation}'.",
            apiItem.SourceItemId, entity.Location, apiItem.Location);
        entity.Location = apiItem.Location;
    }

    /// <summary>
    /// Brings an item's stored listings in line with the payload by diffing on the SkuLabs listing id.
    /// Diffed rather than replaced wholesale so an unchanged listing does not churn its row.
    /// </summary>
    private void SyncListings(
        SkulabsItemEntity entity,
        SkulabsApiItem apiItem,
        IReadOnlyDictionary<long, Guid> variantLookup,
        ReconciliationAccumulator accumulator)
    {
        var incoming = apiItem.Listings.ToDictionary(listing => listing.ListingId, StringComparer.Ordinal);
        var stored = entity.Listings.ToDictionary(listing => listing.SkulabsSourceListingId, StringComparer.Ordinal);

        foreach (var (listingId, storedListing) in stored)
        {
            if (!incoming.ContainsKey(listingId))
            {
                entity.Listings.Remove(storedListing);
                dbContext.SkulabsItemListings.Remove(storedListing);
            }
        }

        foreach (var (listingId, incomingListing) in incoming)
        {
            if (!stored.TryGetValue(listingId, out var storedListing))
            {
                entity.Listings.Add(BuildListing(incomingListing, variantLookup, accumulator));
                continue;
            }

            RefreshListing(storedListing, incomingListing, variantLookup, accumulator);
        }
    }

    /// <summary>
    /// Updates a listing we already hold. The variant it resolves to can change between runs — either
    /// because SkuLabs re-pointed the listing, or because a variant we did not have has since been
    /// ingested.
    /// </summary>
    private static void RefreshListing(
        SkulabsItemListingEntity stored,
        SkulabsApiListing incoming,
        IReadOnlyDictionary<long, Guid> variantLookup,
        ReconciliationAccumulator accumulator)
    {
        stored.RawVariantId = incoming.RawVariantId;
        stored.ShopifyProductId = incoming.ShopifyProductId;
        stored.ShopifyProductVariantId = ResolveVariant(incoming, variantLookup, accumulator);
    }

    /// <summary>
    /// Writes the variant history for one item's run. Events describe the <em>link</em>, not the
    /// individual listing rows: a variant is only told it was linked or unlinked when the item's
    /// resolved variant actually changed, so shuffling the listings of an item that was never
    /// resolvable stays silent.
    /// <para>
    /// An item that has just become ambiguous says so on every variant it names. Reporting those as
    /// "linked" would put a claim in the merchant-facing history that
    /// <see cref="SkulabsItemLinks.IsSyncable"/> refuses to honour, leaving the variant page and its
    /// own audit trail contradicting each other.
    /// </para>
    /// </summary>
    private void LogLinkTransition(
        string sourceItemId,
        Guid? previousVariantId,
        Guid? currentVariantId,
        int previousListingCount,
        SkulabsItemEntity entity)
    {
        if (previousVariantId != currentVariantId)
        {
            if (previousVariantId is { } lost)
            {
                AddVariantLog(lost, VariantLogMessages.SkulabsUnlinked(sourceItemId));
            }

            if (currentVariantId is { } gained)
            {
                AddVariantLog(gained, VariantLogMessages.SkulabsLinked(sourceItemId));
            }
        }

        if (entity.Listings.Count <= 1 || previousListingCount > 1)
        {
            return;
        }

        foreach (var listing in entity.Listings)
        {
            if (listing.ShopifyProductVariantId is { } variantGuid)
            {
                AddVariantLog(
                    variantGuid,
                    VariantLogMessages.SkulabsListedAmbiguously(sourceItemId, entity.Listings.Count));
            }
        }
    }

    private SkulabsItemListingEntity BuildListing(
        SkulabsApiListing listing,
        IReadOnlyDictionary<long, Guid> variantLookup,
        ReconciliationAccumulator accumulator) =>
        new()
        {
            SkulabsSourceListingId = listing.ListingId,
            RawVariantId = listing.RawVariantId,
            ShopifyProductId = listing.ShopifyProductId,
            ShopifyProductVariantId = ResolveVariant(listing, variantLookup, accumulator)
        };

    private static Guid? ResolveVariant(
        SkulabsApiListing listing,
        IReadOnlyDictionary<long, Guid> variantLookup,
        ReconciliationAccumulator accumulator)
    {
        if (long.TryParse(listing.RawVariantId, out var variantId)
            && variantLookup.TryGetValue(variantId, out var guid))
        {
            return guid;
        }

        accumulator.UnresolvedListings++;
        return null;
    }

    /// <summary>
    /// Deletes rows for items SkuLabs no longer reports at all. Their listings go with them by
    /// cascade, and a variant that was actually linked gets an unlink event so the history records
    /// why the link disappeared. An ambiguous item leaves silently — it never held a link to lose.
    /// </summary>
    private void RemoveItemsNoLongerReported(
        SkulabsItemCollection collection,
        IReadOnlyDictionary<string, SkulabsItemEntity> existing,
        ReconciliationAccumulator accumulator)
    {
        var reported = collection.Items
            .Select(item => item.SourceItemId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (sourceItemId, entity) in existing)
        {
            if (reported.Contains(sourceItemId))
            {
                continue;
            }

            if (ResolvedVariantId(entity.Listings) is { } variantGuid)
            {
                AddVariantLog(variantGuid, VariantLogMessages.SkulabsUnlinked(sourceItemId));
            }

            dbContext.SkulabsItems.Remove(entity);
            accumulator.Removed++;
            logger.LogDebug("Removing SkuLabs item {SkulabsItemId}; SkuLabs no longer reports it.", sourceItemId);
        }
    }

    /// <summary>
    /// The variant an item currently resolves to, or null when it has no listings or more than one.
    /// This is the item side of the link only — whether the variant accepts it depends on
    /// <see cref="SkulabsItemLinks.IsSyncable"/>, which the read paths apply.
    /// </summary>
    private static Guid? ResolvedVariantId(ICollection<SkulabsItemListingEntity> listings) =>
        listings.Count == 1 ? listings.Single().ShopifyProductVariantId : null;

    private void AddVariantLog(Guid variantGuid, string message)
    {
        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = variantGuid,
            Message = message
        });
    }

    /// <summary>
    /// Tally of reconciliation outcomes for a single <see cref="Sync"/> run.
    /// </summary>
    private sealed class ReconciliationAccumulator
    {
        public List<Guid> Created { get; } = [];
        public List<Guid> Updated { get; } = [];
        public int UnresolvedListings { get; set; }
        public int Skipped { get; set; }
        public int Removed { get; set; }
        public int Ambiguous { get; private set; }

        public void CountCardinality(int listingCount)
        {
            if (listingCount > 1)
            {
                Ambiguous++;
            }
        }

        public SkulabsItemSyncResult ToResult() => new(
            Created, Updated, UnresolvedListings, Skipped, Removed, Ambiguous);
    }
}
