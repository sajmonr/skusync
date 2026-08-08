using Application.Sync;
using Application.Sync.Merge;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Products.Services;

public class ProductsService(
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext,
    ILogger<ProductsService> logger,
    IReconciler reconciler,
    IShopifyDispatchTrigger dispatchTrigger) : IProductsService
{
    public async Task SyncProducts(CancellationToken cancellationToken = default)
    {
        var import = await ImportProductsFromShopify();
        if (!import.IsSuccess)
        {
            throw new InvalidOperationException($"Shopify product import failed: {import.Error}");
        }

        var deduplication = await DeduplicateProducts();
        if (!deduplication.IsSuccess)
        {
            throw new InvalidOperationException($"Product deduplication failed: {deduplication.Error}");
        }
    }

    public async Task<ProductImportResult> ImportProductsFromShopify()
    {
        logger.LogDebug("Starting Shopify product synchronization.");

        ShopifyProductVariant[] shopifyVariants;
        try
        {
            shopifyVariants = await shopifyProductService.GetProducts();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while fetching products from Shopify.");
            return ProductImportResult.Failure(
                "Could not import products from Shopify because the products could not be fetched.");
        }

        logger.LogDebug("Fetched {Count} product variants from Shopify.", shopifyVariants.Length);

        var dbVariantsByGlobalId = await dbContext.ShopifyProductVariants
            .ToDictionaryAsync(v => v.GlobalVariantId);

        logger.LogDebug("Found {Count} product variants in the database.", dbVariantsByGlobalId.Count);

        List<ShopifyProductVariantEntity> createdEntities;
        List<ShopifyProductVariantEntity> updatedEntities;
        List<ShopifyProductVariantEntity> seenEntities;
        int deletedCount;
        try
        {
            (createdEntities, updatedEntities, seenEntities, deletedCount) =
                MirrorVariants(shopifyVariants, dbVariantsByGlobalId);
        }
        catch (Exception exception)
        {
            // Includes failures from the SKU generator (e.g. DB error during its uniqueness
            // check, or unfittable MaxLength config). Returning a failure result keeps the
            // contract symmetric with the other failure paths above and prevents the loop
            // exception from poisoning the Quartz job.
            logger.LogError(exception, "An exception occurred while reconciling Shopify variants in memory.");
            return ProductImportResult.Failure(
                "Could not import products from Shopify because variant reconciliation failed.");
        }

        IReadOnlySet<ShopifyProductVariantEntity> droppedInserts;
        try
        {
            droppedInserts = await dbContext.SaveChangesToleratingVariantConflicts(logger);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while saving product variants to the database.");
            return ProductImportResult.Failure(
                "Could not import products from Shopify because the product variants could not be saved to the database.");
        }

        // Variants a concurrent writer beat us to were dropped from the insert; don't count or
        // dispatch them here — the writer that won the race handles its own rows.
        createdEntities.RemoveAll(droppedInserts.Contains);
        seenEntities.RemoveAll(droppedInserts.Contains);

        var touchedVariantIds = seenEntities
            .Select(e => e.ShopifyProductVariantId)
            .ToArray();

        try
        {
            // Import, not WebhookCreate: a payload SKU seen here is honoured rather than replaced,
            // because a SKU regenerated now would not match the one generated when the variant was
            // first created — the product may since have been renamed, and the SKU derives from the
            // name. See the SKU merge rule.
            await reconciler.ReconcileVariants(touchedVariantIds, MergeOrigin.Import);
        }
        catch (Exception exception)
        {
            // SKU generation runs here now rather than while mirroring, so its failures — a database
            // error during the uniqueness check, an unfittable length configuration — surface at
            // this point. The mirrors are already committed and correct; only the decisions are
            // missing, and the next reconcile will make them. Report a failure rather than letting
            // it escape and poison the job.
            logger.LogError(exception, "An exception occurred while reconciling imported variants.");
            return ProductImportResult.Failure(
                "Imported the product variants but could not reconcile them.");
        }

        // Push whatever the reconcile left pending toward Shopify right away rather than waiting
        // for the scheduled run. Asked for by name rather than handing over everything seen: an
        // import that changed nothing should not look like a dispatch, and on a full catalogue the
        // difference is thousands of ids the dispatcher would only filter back out.
        var pendingVariantIds = await dbContext.ShopifyProductVariants
            .Where(variant => touchedVariantIds.Contains(variant.ShopifyProductVariantId)
                              && variant.PendingShopifySync)
            .Select(variant => variant.ShopifyProductVariantId)
            .ToArrayAsync();

        await dispatchTrigger.TryDispatch(pendingVariantIds);

        logger.LogDebug("Synchronization complete. Created: {Created}, Updated: {Updated}, Deleted: {Deleted}.",
            createdEntities.Count, updatedEntities.Count, deletedCount);

        return ProductImportResult.Success(createdEntities.Count, updatedEntities.Count);
    }

    /// <summary>
    /// Walks the Shopify variant set and partitions it into created and updated entities,
    /// tracking entities in the DbContext as a side effect but not yet calling SaveChanges.
    /// Local variants absent from the (authoritative, complete) Shopify set are marked deleted.
    /// </summary>
    private (List<ShopifyProductVariantEntity> Created, List<ShopifyProductVariantEntity> Updated,
        List<ShopifyProductVariantEntity> Seen, int Deleted)
        MirrorVariants(
            ShopifyProductVariant[] shopifyVariants,
            IReadOnlyDictionary<string, ShopifyProductVariantEntity> dbVariantsByGlobalId)
    {
        var createdEntities = new List<ShopifyProductVariantEntity>();
        var updatedEntities = new List<ShopifyProductVariantEntity>();
        // Every variant this run matched or created, changed or not. Reconcile needs all of them:
        // a variant whose mirror already agrees with Shopify reports no change, yet may still have
        // nothing decided for it — a blank SKU that needs generating, say. Reconciling only the
        // changed ones would leave it waiting for the nightly sweep.
        var seenEntities = new List<ShopifyProductVariantEntity>();
        // Variants created earlier in this same batch, keyed by GlobalVariantId. Shopify can
        // return the same variant more than once in a single payload; without this guard each
        // repeat would queue another insert and violate the unique index on GlobalVariantId.
        var createdByGlobalId = new Dictionary<string, ShopifyProductVariantEntity>(StringComparer.Ordinal);
        // Every GlobalVariantId Shopify returned this run. Any tracked DB variant not in this set
        // is absent from Shopify and gets marked deleted below.
        var seenGlobalVariantIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var shopifyVariant in shopifyVariants)
        {
            seenGlobalVariantIds.Add(shopifyVariant.GlobalVariantId);

            if (dbVariantsByGlobalId.TryGetValue(shopifyVariant.GlobalVariantId, out var existing))
            {
                // Deletion is terminal: a row we've marked deleted stays frozen even if the
                // import happens to surface its id again. Never resurrect or mutate it.
                if (existing.IsDeleted)
                {
                    logger.LogDebug(
                        "Skipping variant {GlobalVariantId} during import; it is marked deleted.",
                        shopifyVariant.GlobalVariantId);
                    continue;
                }

                seenEntities.Add(existing);
                if (TryApplyVariantUpdate(existing, shopifyVariant))
                {
                    updatedEntities.Add(existing);
                }
            }
            else if (createdByGlobalId.TryGetValue(shopifyVariant.GlobalVariantId, out var alreadyCreated))
            {
                // Repeat of a variant already created in this batch — fold any later values
                // into the pending insert instead of queueing a duplicate row.
                logger.LogDebug(
                    "Shopify returned GlobalVariantId {GlobalVariantId} more than once in this batch; merging into the pending insert.",
                    shopifyVariant.GlobalVariantId);
                TryApplyVariantUpdate(alreadyCreated, shopifyVariant);
            }
            else
            {
                var created = CreateNewVariant(shopifyVariant);
                createdEntities.Add(created);
                seenEntities.Add(created);
                createdByGlobalId[shopifyVariant.GlobalVariantId] = created;
            }
        }

        var deletedCount = MarkVariantsRemovedFromShopify(
            shopifyVariants, dbVariantsByGlobalId, seenGlobalVariantIds);

        return (createdEntities, updatedEntities, seenEntities, deletedCount);
    }

    /// <summary>
    /// Marks every locally-tracked variant absent from the Shopify variant set as deleted. The
    /// set from <see cref="IShopifyProductService.GetProducts"/> is authoritative and complete
    /// (fully paginated, and a failed fetch throws rather than returning an empty set), so a
    /// stored variant not present has been removed in Shopify. Rows are preserved for history;
    /// the flag is terminal.
    /// </summary>
    /// <returns>The number of variants newly marked deleted.</returns>
    private int MarkVariantsRemovedFromShopify(
        ShopifyProductVariant[] shopifyVariants,
        IReadOnlyDictionary<string, ShopifyProductVariantEntity> dbVariantsByGlobalId,
        ISet<string> seenGlobalVariantIds)
    {
        var liveVariantCount = dbVariantsByGlobalId.Values.Count(variant => !variant.IsDeleted);

        // Defence-in-depth: a fetch that returns zero variants while we still hold live ones is
        // far more likely a misconfiguration (wrong shop, revoked token) than a genuinely emptied
        // catalogue. Because deletion is terminal, refuse to wipe every variant on an empty fetch.
        // A real fetch failure already throws upstream and never reaches this point.
        if (shopifyVariants.Length == 0 && liveVariantCount > 0)
        {
            logger.LogWarning(
                "Shopify returned zero variants while {LiveCount} live variant(s) exist locally. Skipping removal reconciliation to avoid deleting the entire catalogue on a suspected misconfiguration.",
                liveVariantCount);
            return 0;
        }

        var deletedCount = 0;
        foreach (var (globalVariantId, entity) in dbVariantsByGlobalId)
        {
            if (entity.IsDeleted || seenGlobalVariantIds.Contains(globalVariantId))
            {
                continue;
            }

            entity.IsDeleted = true;
            entity.DeletedOn = DateTime.UtcNow;
            entity.UpdatedOnUtc = DateTime.UtcNow;

            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = entity.ShopifyProductVariantId,
                Message = VariantLogMessages.DeletedFromShopify()
            });

            logger.LogInformation(
                "Marking variant {VariantId} (GlobalVariantId {GlobalVariantId}) as deleted; it is absent from the full Shopify import.",
                entity.VariantId, entity.GlobalVariantId);

            deletedCount++;
        }

        return deletedCount;
    }

    /// <summary>
    /// Applies any changes from <paramref name="shopifyVariant"/> to the locally-tracked
    /// <paramref name="existing"/> entity and stamps <see cref="ShopifyProductVariantEntity.UpdatedOnUtc"/>
    /// when something actually changed.
    /// </summary>
    /// <returns><c>true</c> when the entity was modified; <c>false</c> when no fields changed.</returns>
    private bool TryApplyVariantUpdate(
        ShopifyProductVariantEntity existing,
        ShopifyProductVariant shopifyVariant)
    {
        if (!UpdateVariant(existing, shopifyVariant))
        {
            return false;
        }

        logger.LogDebug("Updating variant with GlobalVariantId {GlobalVariantId}.",
            shopifyVariant.GlobalVariantId);
        return true;
    }

    /// <summary>
    /// Builds a new <see cref="ShopifyProductVariantEntity"/> from the Shopify payload —
    /// synthesising a SKU when Shopify provides none — and adds it to the DbContext.
    /// The matching <c>VariantCreated</c> (and optional <c>SkuSet</c>) log events are
    /// attached to the entity.
    /// </summary>
    private ShopifyProductVariantEntity CreateNewVariant(ShopifyProductVariant shopifyVariant)
    {
        var newVariant = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.CreateVersion7(),
            GlobalProductId = shopifyVariant.GlobalProductId,
            ProductId = shopifyVariant.ProductId,
            GlobalVariantId = shopifyVariant.GlobalVariantId,
            VariantId = shopifyVariant.VariantId,
            DisplayName = shopifyVariant.DisplayName,
            ProductTitle = shopifyVariant.ProductTitle ?? "",
            VariantTitle = shopifyVariant.VariantTitle ?? "",
            // Verbatim, including blanks. A generated SKU written here would make the row claim
            // Shopify holds one when it does not, and the reconciler reads exactly this row to
            // decide what Shopify is owed. Generation happens in the merge rules instead.
            Sku = shopifyVariant.Sku ?? "",
            Barcode = shopifyVariant.Barcode ?? ""
        };

        newVariant.LogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            Message = VariantLogMessages.VariantCreated()
        });

        dbContext.ShopifyProductVariants.Add(newVariant);
        logger.LogDebug("Creating new variant with GlobalVariantId {GlobalVariantId}.",
            shopifyVariant.GlobalVariantId);

        return newVariant;
    }

    public async Task<ProductDeduplicationResult> DeduplicateProducts()
    {
        logger.LogInformation("Starting product deduplication.");

        // Collisions are looked for among the values we intend to push, not the ones the mirrors
        // currently hold. Two variants whose desired SKUs match will collide the moment both are
        // dispatched, whereas a clash between stale mirror values may already be on its way out.
        HashSet<string> duplicateSkus;
        HashSet<string> duplicateBarcodes;
        try
        {
            // Deleted rows are excluded: a dead variant's SKU/barcode must not be counted as a
            // collision that would force a live variant to be renamed, and deleted rows are frozen.
            var desired = dbContext.DesiredItemStates
                .Where(state => !state.ShopifyProductVariant!.IsDeleted);

            duplicateSkus = (await desired
                .GroupBy(state => state.Sku)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync())
                .ToHashSet();

            duplicateBarcodes = (await desired
                .GroupBy(state => state.Barcode)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync())
                .ToHashSet();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while identifying duplicate product variants in the database.");
            return ProductDeduplicationResult.Failure(
                "Could not deduplicate products because duplicate variants could not be identified in the database.");
        }

        logger.LogDebug("Found {SkuCount} duplicate SKU value(s) and {BarcodeCount} duplicate barcode value(s).",
            duplicateSkus.Count, duplicateBarcodes.Count);

        if (duplicateSkus.Count == 0 && duplicateBarcodes.Count == 0)
        {
            logger.LogInformation("No duplicate SKUs or barcodes found. Deduplication complete.");
            return ProductDeduplicationResult.Success([]);
        }

        // Load only the affected rows — variants whose desired SKU or barcode is a duplicated value.
        List<DesiredItemStateEntity> affected;
        try
        {
            affected = await dbContext.DesiredItemStates
                .Include(state => state.ShopifyProductVariant)
                .Where(state => !state.ShopifyProductVariant!.IsDeleted
                                && (duplicateSkus.Contains(state.Sku)
                                    || duplicateBarcodes.Contains(state.Barcode)))
                .ToListAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while fetching duplicate product variants from the database.");
            return ProductDeduplicationResult.Failure(
                "Could not deduplicate products because the duplicate variants could not be fetched from the database.");
        }

        logger.LogInformation("Deduplicating {Count} variant(s).", affected.Count);
        ApplyDeduplication(affected, duplicateSkus, duplicateBarcodes);

        var affectedVariantIds = affected.Select(state => state.ShopifyProductVariant!.VariantId).ToArray();

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while saving deduplicated product variants to the database.");
            return ProductDeduplicationResult.Failure(
                "Could not deduplicate products because the changes could not be saved to the database.");
        }

        logger.LogInformation("Deduplication complete. Modified {Count} variant(s).", affectedVariantIds.Length);

        // The rewritten SKUs/barcodes are divergences we originated; push them right away.
        await dispatchTrigger.TryDispatch(
            affected.Select(state => state.ShopifyProductVariantId).ToArray());

        return ProductDeduplicationResult.Success(affectedVariantIds);
    }

    /// <summary>
    /// Rewrites colliding codes to the Shopify variant id — unique by construction, and stable, so
    /// re-running deduplication cannot keep churning the same rows.
    /// <para>
    /// Writes to the desired state rather than the mirrors. A collision is a decision about what the
    /// variants ought to hold, and the merge rules then leave it alone: a value already decided
    /// stands unless SkuLabs asserts one of its own, which outranks a mere uniqueness fix because it
    /// may already be printed on a label.
    /// </para>
    /// </summary>
    private void ApplyDeduplication(
        List<DesiredItemStateEntity> affected,
        HashSet<string> duplicateSkus,
        HashSet<string> duplicateBarcodes)
    {
        foreach (var desired in affected)
        {
            var variant = desired.ShopifyProductVariant!;
            var replacement = variant.VariantId.ToString();
            var hasDupeSku = duplicateSkus.Contains(desired.Sku);
            var hasDupeBarcode = duplicateBarcodes.Contains(desired.Barcode);

            logger.LogDebug(
                "Deduplicating variant {VariantId}: overwriting {Fields} with variant ID.",
                variant.VariantId,
                hasDupeSku && hasDupeBarcode ? "SKU and barcode" : hasDupeSku ? "SKU" : "barcode");

            if (hasDupeSku)
            {
                dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    ShopifyProductVariantId = variant.ShopifyProductVariantId,
                    Message = VariantLogMessages.SkuUpdated(desired.Sku, replacement)
                });
                desired.Sku = replacement;
            }

            if (hasDupeBarcode)
            {
                dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    ShopifyProductVariantId = variant.ShopifyProductVariantId,
                    Message = VariantLogMessages.BarcodeUpdated(desired.Barcode, replacement)
                });
                desired.Barcode = replacement;
            }

            // The rewritten value is a divergence we originated — Shopify still has the duplicate.
            variant.PendingShopifySync = true;
            variant.UpdatedOnUtc = DateTime.UtcNow;
            desired.UpdatedOnUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Refreshes the mirror from the Shopify payload. A straight copy of all three fields — what
    /// they <em>should</em> be is decided later, by the merge rules, against this row.
    /// </summary>
    private bool UpdateVariant(
        ShopifyProductVariantEntity existing,
        ShopifyProductVariant shopifyVariant)
    {
        var changed = false;

        if (existing.DisplayName != shopifyVariant.DisplayName)
        {
            var oldDisplayName = existing.DisplayName;
            existing.DisplayName = shopifyVariant.DisplayName;
            changed = true;
            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = existing.ShopifyProductVariantId,
                Message = VariantLogMessages.TitleUpdated(oldDisplayName, shopifyVariant.DisplayName)
            });
        }

        var incomingSku = shopifyVariant.Sku ?? "";
        if (!string.Equals(existing.Sku, incomingSku, StringComparison.Ordinal))
        {
            existing.Sku = incomingSku;
            changed = true;
        }

        var incomingBarcode = shopifyVariant.Barcode ?? "";
        if (!string.Equals(existing.Barcode, incomingBarcode, StringComparison.Ordinal))
        {
            existing.Barcode = incomingBarcode;
            changed = true;
        }

        if (changed)
        {
            existing.UpdatedOnUtc = DateTime.UtcNow;
        }

        return changed;
    }
}
