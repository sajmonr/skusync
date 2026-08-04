using Application.Skus;
using Application.Sync;
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
    IShopifyDispatchTrigger dispatchTrigger,
    ISkuGenerator skuGenerator) : IProductsService
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
        int deletedCount;
        try
        {
            (createdEntities, updatedEntities, deletedCount) =
                await ReconcileVariants(shopifyVariants, dbVariantsByGlobalId);
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

        // Push whatever this import marked pending (generated SKUs, Shopify-side drift) toward
        // Shopify right away instead of waiting for the scheduled dispatch run. The dispatcher
        // batches per product and skips rows that aren't pending.
        await dispatchTrigger.TryDispatch(createdEntities
            .Concat(updatedEntities)
            .Select(e => e.ShopifyProductVariantId)
            .ToArray());

        logger.LogDebug("Synchronization complete. Created: {Created}, Updated: {Updated}, Deleted: {Deleted}.",
            createdEntities.Count, updatedEntities.Count, deletedCount);

        return ProductImportResult.Success(createdEntities.Count, updatedEntities.Count);
    }

    /// <summary>
    /// Walks the Shopify variant set and partitions it into created and updated entities,
    /// tracking entities in the DbContext as a side effect but not yet calling SaveChanges.
    /// Local variants absent from the (authoritative, complete) Shopify set are marked deleted.
    /// </summary>
    private async Task<(List<ShopifyProductVariantEntity> Created, List<ShopifyProductVariantEntity> Updated, int Deleted)>
        ReconcileVariants(
            ShopifyProductVariant[] shopifyVariants,
            IReadOnlyDictionary<string, ShopifyProductVariantEntity> dbVariantsByGlobalId)
    {
        var createdEntities = new List<ShopifyProductVariantEntity>();
        var updatedEntities = new List<ShopifyProductVariantEntity>();
        // SKUs generated in this batch but not yet persisted — kept so two variants in
        // the same import cannot be issued the same generated SKU.
        var reservedSkus = new HashSet<string>(StringComparer.Ordinal);
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

                if (await TryApplyVariantUpdate(existing, shopifyVariant, reservedSkus))
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
                await TryApplyVariantUpdate(alreadyCreated, shopifyVariant, reservedSkus);
            }
            else
            {
                var created = await CreateNewVariant(shopifyVariant, reservedSkus);
                createdEntities.Add(created);
                createdByGlobalId[shopifyVariant.GlobalVariantId] = created;
            }
        }

        var deletedCount = MarkVariantsRemovedFromShopify(
            shopifyVariants, dbVariantsByGlobalId, seenGlobalVariantIds);

        return (createdEntities, updatedEntities, deletedCount);
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
    private async Task<bool> TryApplyVariantUpdate(
        ShopifyProductVariantEntity existing,
        ShopifyProductVariant shopifyVariant,
        ISet<string> reservedSkus)
    {
        var changed = await UpdateVariant(existing, shopifyVariant, reservedSkus);
        if (!changed)
        {
            return false;
        }

        existing.UpdatedOnUtc = DateTime.UtcNow;
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
    private async Task<ShopifyProductVariantEntity> CreateNewVariant(
        ShopifyProductVariant shopifyVariant,
        ISet<string> reservedSkus)
    {
        var (sku, skuWasGenerated) = await ResolveSkuForNewVariant(shopifyVariant, reservedSkus);

        var newVariant = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.CreateVersion7(),
            GlobalProductId = shopifyVariant.GlobalProductId,
            ProductId = shopifyVariant.ProductId,
            GlobalVariantId = shopifyVariant.GlobalVariantId,
            VariantId = shopifyVariant.VariantId,
            DisplayName = shopifyVariant.DisplayName,
            Sku = sku,
            Barcode = shopifyVariant.Barcode,
            // A generated SKU is a divergence we originated — Shopify doesn't have it yet. A SKU
            // taken from the Shopify payload matches Shopify, so nothing is pending.
            PendingShopifySync = skuWasGenerated
        };

        newVariant.LogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            Message = VariantLogMessages.VariantCreated()
        });
        if (skuWasGenerated)
        {
            newVariant.LogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                Message = VariantLogMessages.SkuSet(sku)
            });
        }

        dbContext.ShopifyProductVariants.Add(newVariant);
        logger.LogDebug("Creating new variant with GlobalVariantId {GlobalVariantId}.",
            shopifyVariant.GlobalVariantId);

        return newVariant;
    }

    /// <summary>
    /// Returns the SKU to use for a brand-new variant: Shopify's own SKU when present,
    /// otherwise one synthesised by <see cref="ISkuGenerator"/>. The returned flag tells
    /// callers whether a <c>SkuSet</c> log event should be emitted (only when generated).
    /// </summary>
    private async Task<(string Sku, bool WasGenerated)> ResolveSkuForNewVariant(
        ShopifyProductVariant shopifyVariant,
        ISet<string> reservedSkus)
    {
        if (!string.IsNullOrWhiteSpace(shopifyVariant.Sku))
        {
            return (shopifyVariant.Sku, WasGenerated: false);
        }

        var sku = await skuGenerator.Generate(
            shopifyVariant.ProductTitle, shopifyVariant.VariantTitle, reservedSkus,
            fallbackSegment: shopifyVariant.VariantId.ToString());
        reservedSkus.Add(sku);
        logger.LogInformation(
            "Shopify variant {GlobalVariantId} had no SKU; assigning generated SKU '{Sku}'.",
            shopifyVariant.GlobalVariantId, sku);
        return (sku, WasGenerated: true);
    }

    public async Task<ProductDeduplicationResult> DeduplicateProducts()
    {
        logger.LogInformation("Starting product deduplication.");

        // Find which SKU and barcode values are shared by more than one variant — entirely in the database.
        HashSet<string> duplicateSkus;
        HashSet<string> duplicateBarcodes;
        try
        {
            // Deleted rows are excluded: a dead variant's SKU/barcode must not be counted as a
            // collision that would force a live variant to be renamed, and deleted rows are frozen.
            duplicateSkus = (await dbContext.ShopifyProductVariants
                .Where(v => !v.IsDeleted)
                .GroupBy(v => v.Sku)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync())
                .ToHashSet();

            duplicateBarcodes = (await dbContext.ShopifyProductVariants
                .Where(v => !v.IsDeleted)
                .GroupBy(v => v.Barcode)
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

        // Load only the affected rows — variants whose SKU or barcode is one of the duplicated values.
        List<ShopifyProductVariantEntity> variants;
        try
        {
            variants = await dbContext.ShopifyProductVariants
                .Where(v => !v.IsDeleted
                            && (duplicateSkus.Contains(v.Sku) || duplicateBarcodes.Contains(v.Barcode)))
                .ToListAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while fetching duplicate product variants from the database.");
            return ProductDeduplicationResult.Failure(
                "Could not deduplicate products because the duplicate variants could not be fetched from the database.");
        }

        logger.LogInformation("Deduplicating {Count} variant(s).", variants.Count);
        ApplyDeduplication(variants, duplicateSkus, duplicateBarcodes);

        var affectedVariantIds = variants.Select(v => v.VariantId).ToArray();

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
        await dispatchTrigger.TryDispatch(variants.Select(v => v.ShopifyProductVariantId).ToArray());

        return ProductDeduplicationResult.Success(affectedVariantIds);
    }

    private void ApplyDeduplication(
        List<ShopifyProductVariantEntity> variants,
        HashSet<string> duplicateSkus,
        HashSet<string> duplicateBarcodes)
    {
        foreach (var variant in variants)
        {
            var hasDupeSku = duplicateSkus.Contains(variant.Sku);
            var hasDupeBarcode = duplicateBarcodes.Contains(variant.Barcode);

            logger.LogDebug(
                "Deduplicating variant {VariantId}: overwriting {Fields} with variant ID.",
                variant.VariantId,
                hasDupeSku && hasDupeBarcode ? "SKU and barcode" : hasDupeSku ? "SKU" : "barcode");

            if (hasDupeSku)
            {
                var oldSku = variant.Sku;
                variant.Sku = variant.VariantId.ToString();
                dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    ShopifyProductVariantId = variant.ShopifyProductVariantId,
                    Message = VariantLogMessages.SkuUpdated(oldSku, variant.VariantId.ToString())
                });
            }

            if (hasDupeBarcode)
            {
                var oldBarcode = variant.Barcode;
                variant.Barcode = variant.VariantId.ToString();
                dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    ShopifyProductVariantId = variant.ShopifyProductVariantId,
                    Message = VariantLogMessages.BarcodeUpdated(oldBarcode, variant.VariantId.ToString())
                });
            }

            // The rewritten value is a divergence we originated — Shopify still has the duplicate.
            variant.PendingShopifySync = true;
            variant.UpdatedOnUtc = DateTime.UtcNow;
        }
    }

    private async Task<bool> UpdateVariant(
        ShopifyProductVariantEntity existing,
        ShopifyProductVariant shopifyVariant,
        ISet<string> reservedSkus)
    {
        var changed = false;

        if(existing.DisplayName != shopifyVariant.DisplayName)
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

        if (string.IsNullOrWhiteSpace(existing.Sku))
        {
            // Prefer the Shopify-provided SKU when present; otherwise synthesize one
            // so the variant doesn't sit in the database without an identifier.
            string newSku;
            if (!string.IsNullOrWhiteSpace(shopifyVariant.Sku))
            {
                newSku = shopifyVariant.Sku;
            }
            else
            {
                newSku = await skuGenerator.Generate(
                    shopifyVariant.ProductTitle, shopifyVariant.VariantTitle, reservedSkus,
                    fallbackSegment: shopifyVariant.VariantId.ToString());
                reservedSkus.Add(newSku);
            }

            existing.Sku = newSku;
            changed = true;
            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = existing.ShopifyProductVariantId,
                Message = VariantLogMessages.SkuSet(newSku)
            });
        }

        if (string.IsNullOrWhiteSpace(existing.Barcode) && !string.IsNullOrWhiteSpace(shopifyVariant.Barcode))
        {
            existing.Barcode = shopifyVariant.Barcode;
            changed = true;
            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = existing.ShopifyProductVariantId,
                Message = VariantLogMessages.BarcodeSet(shopifyVariant.Barcode)
            });
        }

        // Shopify's SKU/barcode differs from our authoritative local values — covers both a SKU
        // we just generated (Shopify sent none) and Shopify-side drift. Either way the variant
        // needs a push to bring Shopify back in line.
        if (existing.Sku != shopifyVariant.Sku || existing.Barcode != shopifyVariant.Barcode)
        {
            existing.PendingShopifySync = true;
            changed = true;
        }

        return changed;
    }
}
