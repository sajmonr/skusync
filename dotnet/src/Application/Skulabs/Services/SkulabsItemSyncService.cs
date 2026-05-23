using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Skulabs.Services;

/// <summary>
/// Reconciles the local SkuLabs item table with the SkuLabs API by considering only the
/// link identifiers — Shopify variant id on one side, SkuLabs source item id on the other.
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

        var apiItems = await skulabsClient.GetAllItems();
        logger.LogDebug("Reconciler received {Count} usable item(s) from the SkuLabs client.", apiItems.Length);

        if (apiItems.Length == 0)
        {
            logger.LogInformation("SkuLabs returned no usable items. Sync finished with nothing to do.");
            return SkulabsItemSyncResult.Empty;
        }

        var variantLookup = await LoadVariantLookupAsync(cancellationToken);
        var indexes = await LoadExistingItemIndexesAsync(cancellationToken);
        logger.LogDebug(
            "Loaded {VariantCount} Shopify variant(s) and {ExistingItemCount} existing SkuLabs item(s) from the database.",
            variantLookup.Count, indexes.Count);

        var accumulator = new ReconciliationAccumulator();
        foreach (var apiItem in apiItems)
        {
            ReconcileItem(apiItem, variantLookup, indexes, accumulator);
        }

        logger.LogDebug(
            "Reconciliation done. About to persist — Created: {Created}, Re-linked: {Relinked}, Severed: {Severed}, Unmatched: {Unmatched}, Skipped: {Skipped}.",
            accumulator.Created.Count, accumulator.Updated.Count, accumulator.Severed,
            accumulator.Unmatched, accumulator.Skipped);

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
            "SkuLabs item sync finished. Created: {Created}, Re-linked: {Relinked}, Severed: {Severed}, Unmatched: {Unmatched}, Skipped: {Skipped}.",
            accumulator.Created.Count, accumulator.Updated.Count, accumulator.Severed,
            accumulator.Unmatched, accumulator.Skipped);

        return accumulator.ToResult();
    }

    /// <summary>
    /// Loads a lookup from Shopify's numeric variant id to the local database Guid. Only the
    /// two columns are projected so this stays cheap even with thousands of variants.
    /// </summary>
    private Task<Dictionary<long, Guid>> LoadVariantLookupAsync(CancellationToken cancellationToken) =>
        dbContext.ShopifyProductVariants
            .Select(v => new { v.VariantId, v.ShopifyProductVariantId })
            .ToDictionaryAsync(v => v.VariantId, v => v.ShopifyProductVariantId, cancellationToken);

    /// <summary>
    /// Loads every existing SkuLabs item once and builds two indexes over the same tracked
    /// entity instances — one by SkuLabs source item id, one by variant Guid. Both indexes
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
            if (!TryGetMatchingVariantGuid(apiItem, variantLookup, out var variantGuid, out var nonMatchReason))
            {
                accumulator.RecordNonMatch(nonMatchReason);
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

            // Case C: brand-new SkuLabs item id. If the destination variant already has a
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
    /// Resolves the local variant Guid for a SkuLabs item, or reports why it couldn't be matched.
    /// </summary>
    private bool TryGetMatchingVariantGuid(
        SkuLabsItem apiItem,
        IReadOnlyDictionary<long, Guid> variantLookup,
        out Guid variantGuid,
        out NonMatchReason reason)
    {
        if (!long.TryParse(apiItem.ShopifyVariantId, out var numericVariantId))
        {
            logger.LogWarning(
                "SkuLabs item {SkulabsItemId} has non-numeric Shopify variant id '{ShopifyVariantId}'. Skipping.",
                apiItem.SkulabsItemId, apiItem.ShopifyVariantId);
            variantGuid = default;
            reason = NonMatchReason.Skipped;
            return false;
        }

        if (!variantLookup.TryGetValue(numericVariantId, out variantGuid))
        {
            // Per-item Debug level only — at 2000+ items this would otherwise drown info logs.
            // Aggregate "Unmatched: N" appears in the summary at Information level.
            logger.LogDebug(
                "SkuLabs item {SkulabsItemId} references Shopify variant id {VariantId} which is not in the database. Skipping.",
                apiItem.SkulabsItemId, numericVariantId);
            reason = NonMatchReason.Unmatched;
            return false;
        }

        reason = default;
        return true;
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

    private enum NonMatchReason
    {
        Unmatched,
        Skipped
    }

    /// <summary>
    /// Tally of reconciliation outcomes for a single <see cref="Sync"/> run.
    /// </summary>
    private sealed class ReconciliationAccumulator
    {
        public List<Guid> Created { get; } = [];
        public List<Guid> Updated { get; } = [];
        public int Unmatched { get; private set; }
        public int Skipped { get; set; }
        public int Severed { get; set; }

        public void RecordNonMatch(NonMatchReason reason)
        {
            switch (reason)
            {
                case NonMatchReason.Unmatched: Unmatched++; break;
                case NonMatchReason.Skipped: Skipped++; break;
            }
        }

        public SkulabsItemSyncResult ToResult() => new(Created, Updated, Unmatched, Skipped);
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
