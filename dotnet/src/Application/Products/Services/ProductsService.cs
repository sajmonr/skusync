using Application.Events;
using Application.Products.Events;
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
    IEventAccumulator<ProductChangedEvent> eventAccumulator) : IProductsService
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

        var dbVariantsByGlobalId = await dbContext.ShopifyProductVariants.ToDictionaryAsync(v => v.GlobalVariantId);

        logger.LogDebug("Found {Count} product variants in the database.", dbVariantsByGlobalId.Count);

        var created = 0;
        var updated = 0;

        // Collect events before SaveChangesAsync so we only publish for persisted changes.
        var pendingEvents = new List<ProductChangedEvent>();

        foreach (var shopifyVariant in shopifyVariants)
        {
            if (dbVariantsByGlobalId.TryGetValue(shopifyVariant.GlobalVariantId, out var existing))
            {
                var changed = UpdateVariant(existing, shopifyVariant);

                if (!changed)
                {
                    continue;
                }

                existing.UpdatedOnUtc = DateTime.UtcNow;
                logger.LogDebug("Updating variant with GlobalVariantId {GlobalVariantId}.",
                    shopifyVariant.GlobalVariantId);
                pendingEvents.Add(new ProductChangedEvent(shopifyVariant.VariantId, ProductChangeType.Updated));
                updated++;
            }
            else
            {
                var newVariant = new ShopifyProductVariantEntity
                {
                    ShopifyProductVariantId = Guid.CreateVersion7(),
                    GlobalProductId = shopifyVariant.GlobalProductId,
                    ProductId = shopifyVariant.ProductId,
                    GlobalVariantId = shopifyVariant.GlobalVariantId,
                    VariantId = shopifyVariant.VariantId,
                    ProductTitle = shopifyVariant.ProductTitle,
                    VariantTitle = shopifyVariant.VariantTitle,
                    Sku = shopifyVariant.Sku,
                    Barcode = shopifyVariant.Barcode
                };

                dbContext.ShopifyProductVariants.Add(newVariant);
                logger.LogDebug("Creating new variant with GlobalVariantId {GlobalVariantId}.",
                    shopifyVariant.GlobalVariantId);
                pendingEvents.Add(new ProductChangedEvent(shopifyVariant.VariantId, ProductChangeType.Created));
                created++;
            }
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

        // Enqueue only after a successful save so no phantom events enter the queue.
        eventAccumulator.Enqueue(pendingEvents);

        logger.LogDebug("Synchronization complete. Created: {Created}, Updated: {Updated}.", created, updated);

        return ProductImportResult.Success(created, updated);
    }

    public async Task<ProductDeduplicationResult> DeduplicateProducts()
    {
        List<ShopifyProductVariantEntity> variants;
        try
        {
            variants = await dbContext.ShopifyProductVariants.ToListAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An exception occurred while fetching product variants from the database.");
            return ProductDeduplicationResult.Failure(
                "Could not deduplicate products because the product variants could not be fetched from the database.");
        }

        logger.LogDebug("Fetched {Count} product variant(s) for deduplication analysis.", variants.Count);

        var duplicates = FindDuplicateIds(variants);
        var affectedVariantIds = duplicates.BySkuIds.Union(duplicates.ByBarcodeIds).ToHashSet();

        if (affectedVariantIds.Count == 0)
        {
            logger.LogInformation("No duplicate SKUs or barcodes found. Deduplication complete.");
            return ProductDeduplicationResult.Success([]);
        }

        logger.LogInformation("Deduplicating {Count} variant(s).", affectedVariantIds.Count);
        ApplyDeduplication(variants, duplicates.BySkuIds, duplicates.ByBarcodeIds, affectedVariantIds);

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

        logger.LogInformation("Deduplication complete. Modified {Count} variant(s).", affectedVariantIds.Count);

        return ProductDeduplicationResult.Success(affectedVariantIds.ToArray());
    }

    private DuplicateIds FindDuplicateIds(List<ShopifyProductVariantEntity> variants)
    {
        var bySkuIds = variants
            .GroupBy(v => v.Sku)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(v => v.VariantId))
            .ToHashSet();

        logger.LogDebug("Found {Count} variant(s) with a duplicate SKU.", bySkuIds.Count);

        var byBarcodeIds = variants
            .GroupBy(v => v.Barcode)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(v => v.VariantId))
            .ToHashSet();

        logger.LogDebug("Found {Count} variant(s) with a duplicate barcode.", byBarcodeIds.Count);

        return new DuplicateIds(bySkuIds, byBarcodeIds);
    }

    private record DuplicateIds(HashSet<long> BySkuIds, HashSet<long> ByBarcodeIds);

    private void ApplyDeduplication(
        List<ShopifyProductVariantEntity> variants,
        HashSet<long> duplicateBySkuIds,
        HashSet<long> duplicateByBarcodeIds,
        HashSet<long> affectedVariantIds)
    {
        foreach (var variant in variants.Where(v => affectedVariantIds.Contains(v.VariantId)))
        {
            var hasDupeSku = duplicateBySkuIds.Contains(variant.VariantId);
            var hasDupeBarcode = duplicateByBarcodeIds.Contains(variant.VariantId);

            logger.LogDebug(
                "Deduplicating variant {VariantId}: overwriting {Fields} with variant ID.",
                variant.VariantId,
                hasDupeSku && hasDupeBarcode ? "SKU and barcode" : hasDupeSku ? "SKU" : "barcode");

            if (hasDupeSku)
            {
                variant.Sku = variant.VariantId.ToString();
            }

            if (hasDupeBarcode)
            {
                variant.Barcode = variant.VariantId.ToString();
            }

            variant.UpdatedOnUtc = DateTime.UtcNow;
        }
    }

    private static bool UpdateVariant(ShopifyProductVariantEntity existing, ShopifyProductVariant shopifyVariant)
    {
        var changed = false;

        if (existing.ProductTitle != shopifyVariant.ProductTitle)
        {
            existing.ProductTitle = shopifyVariant.ProductTitle;
            changed = true;
        }

        if (existing.VariantTitle != shopifyVariant.VariantTitle)
        {
            existing.VariantTitle = shopifyVariant.VariantTitle;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(existing.Sku) && !string.IsNullOrWhiteSpace(shopifyVariant.Sku))
        {
            existing.Sku = shopifyVariant.Sku;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(existing.Barcode) && !string.IsNullOrWhiteSpace(shopifyVariant.Barcode))
        {
            existing.Barcode = shopifyVariant.Barcode;
            changed = true;
        }

        return changed;
    }
}