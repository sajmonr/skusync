using Application.Events;
using Application.Products.Events;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Shopify;

public class ShopifyService(
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext,
    ILogger<ShopifyService> logger,
    IEventAccumulator<ProductChangedEvent> eventAccumulator) : IShopifyService
{
    public async Task<ProductImportResult> ImportProducts()
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

        var duplicateBySkuIds = variants
            .GroupBy(v => v.Sku)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(v => v.VariantId))
            .ToHashSet();

        logger.LogDebug("Found {Count} variant(s) with a duplicate SKU.", duplicateBySkuIds.Count);

        var duplicateByBarcodeIds = variants
            .GroupBy(v => v.Barcode)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(v => v.VariantId))
            .ToHashSet();

        logger.LogDebug("Found {Count} variant(s) with a duplicate barcode.", duplicateByBarcodeIds.Count);

        var affectedVariantIds = duplicateBySkuIds.Union(duplicateByBarcodeIds).ToHashSet();

        if (affectedVariantIds.Count == 0)
        {
            logger.LogInformation("No duplicate SKUs or barcodes found. Deduplication complete.");
            return ProductDeduplicationResult.Success([]);
        }

        logger.LogInformation("Deduplicating {Count} variant(s).", affectedVariantIds.Count);

        foreach (var variant in variants.Where(v => affectedVariantIds.Contains(v.VariantId)))
        {
            logger.LogDebug("Deduplicating variant {VariantId}: overwriting SKU and barcode with variant ID.",
                variant.VariantId);
            variant.Sku = variant.VariantId.ToString();
            variant.Barcode = variant.VariantId.ToString();
            variant.UpdatedOnUtc = DateTime.UtcNow;
        }

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