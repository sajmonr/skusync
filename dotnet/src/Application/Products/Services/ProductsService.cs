using Application.Products.Events;
using Application.Skus;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace Application.Products.Services;

public class ProductsService(
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext,
    ILogger<ProductsService> logger,
    IMessageBus messageBus,
    ISkuGenerator skuGenerator) : IProductsService
{
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
        try
        {
            (createdEntities, updatedEntities) =
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

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while saving product variants to the database.");
            return ProductImportResult.Failure(
                "Could not import products from Shopify because the product variants could not be saved to the database.");
        }

        await PublishVariantEvents(createdEntities, updatedEntities);

        logger.LogDebug("Synchronization complete. Created: {Created}, Updated: {Updated}.",
            createdEntities.Count, updatedEntities.Count);

        return ProductImportResult.Success(createdEntities.Count, updatedEntities.Count);
    }

    /// <summary>
    /// Walks the Shopify variant set and partitions it into created and updated entities,
    /// tracking entities in the DbContext as a side effect but not yet calling SaveChanges.
    /// </summary>
    private async Task<(List<ShopifyProductVariantEntity> Created, List<ShopifyProductVariantEntity> Updated)>
        ReconcileVariants(
            ShopifyProductVariant[] shopifyVariants,
            IReadOnlyDictionary<string, ShopifyProductVariantEntity> dbVariantsByGlobalId)
    {
        var createdEntities = new List<ShopifyProductVariantEntity>();
        var updatedEntities = new List<ShopifyProductVariantEntity>();
        // SKUs generated in this batch but not yet persisted — kept so two variants in
        // the same import cannot be issued the same generated SKU.
        var reservedSkus = new HashSet<string>(StringComparer.Ordinal);

        foreach (var shopifyVariant in shopifyVariants)
        {
            if (dbVariantsByGlobalId.TryGetValue(shopifyVariant.GlobalVariantId, out var existing))
            {
                if (await TryApplyVariantUpdate(existing, shopifyVariant, reservedSkus))
                {
                    updatedEntities.Add(existing);
                }
            }
            else
            {
                createdEntities.Add(await CreateNewVariant(shopifyVariant, reservedSkus));
            }
        }

        return (createdEntities, updatedEntities);
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
            Barcode = shopifyVariant.Barcode
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
            shopifyVariant.ProductTitle, shopifyVariant.VariantTitle, reservedSkus);
        reservedSkus.Add(sku);
        logger.LogInformation(
            "Shopify variant {GlobalVariantId} had no SKU; assigning generated SKU '{Sku}'.",
            shopifyVariant.GlobalVariantId, sku);
        return (sku, WasGenerated: true);
    }

    /// <summary>
    /// Publishes one <c>ProductVariantCreated</c>/<c>ProductVariantUpdated</c> event per
    /// persisted entity. Called only after the DbContext save has succeeded, so that no
    /// phantom events ever reach the queue.
    /// </summary>
    private async Task PublishVariantEvents(
        List<ShopifyProductVariantEntity> createdEntities,
        List<ShopifyProductVariantEntity> updatedEntities)
    {
        await messageBus.PublishBatch(
            updatedEntities.Select(e => new ProductVariantUpdatedEvent(e.ShopifyProductVariantId)));
        await messageBus.PublishBatch(
            createdEntities.Select(e => new ProductVariantCreatedEvent(e.ShopifyProductVariantId)));
    }

    public async Task<ProductDeduplicationResult> DeduplicateProducts()
    {
        logger.LogInformation("Starting product deduplication.");

        // Find which SKU and barcode values are shared by more than one variant — entirely in the database.
        HashSet<string> duplicateSkus;
        HashSet<string> duplicateBarcodes;
        try
        {
            duplicateSkus = (await dbContext.ShopifyProductVariants
                .GroupBy(v => v.Sku)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync())
                .ToHashSet();

            duplicateBarcodes = (await dbContext.ShopifyProductVariants
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
                .Where(v => duplicateSkus.Contains(v.Sku) || duplicateBarcodes.Contains(v.Barcode))
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
                    shopifyVariant.ProductTitle, shopifyVariant.VariantTitle, reservedSkus);
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

        // The below will force change to run
        if (existing.Sku != shopifyVariant.Sku || existing.Barcode != shopifyVariant.Barcode)
        {
            changed = true;
        }

        return changed;
    }
}