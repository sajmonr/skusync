using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Shopify;

public class ShopifyService(
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext,
    ILogger<ShopifyService> logger) : IShopifyService
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

        logger.LogDebug("Synchronization complete. Created: {Created}, Updated: {Updated}.", created, updated);
        
        return ProductImportResult.Success(created, updated);
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