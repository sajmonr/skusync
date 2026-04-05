using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Shopify;

public class ShopifySyncService(
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext,
    ILogger<ShopifySyncService> logger) : IShopifySyncService
{
    public async Task SynchronizeProducts()
    {
        logger.LogDebug("Starting Shopify product synchronization.");

        ShopifyProductVariant[] shopifyVariants;
        try
        {
            shopifyVariants = await shopifyProductService.GetProducts();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch products from Shopify during synchronization.");
            throw;
        }

        logger.LogDebug("Fetched {Count} product variants from Shopify.", shopifyVariants.Length);

        var dbVariants = await dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();

        logger.LogDebug("Found {Count} product variants in the database.", dbVariants.Count);

        var dbVariantsByGlobalId = dbVariants.ToDictionary(v => v.GlobalVariantId);

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
                logger.LogDebug("Updating variant with GlobalVariantId {GlobalVariantId}.", shopifyVariant.GlobalVariantId);
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
                    Title = shopifyVariant.Title,
                    Sku = shopifyVariant.Sku,
                    Barcode = shopifyVariant.Barcode
                };

                dbContext.Set<ShopifyProductVariantEntity>().Add(newVariant);
                logger.LogDebug("Creating new variant with GlobalVariantId {GlobalVariantId}.", shopifyVariant.GlobalVariantId);
                created++;
            }
        }

        await dbContext.SaveChangesAsync();

        logger.LogDebug("Synchronization complete. Created: {Created}, Updated: {Updated}.", created, updated);
    }
    
    private static bool UpdateVariant(ShopifyProductVariantEntity existing, ShopifyProductVariant shopifyVariant)
    {
        var changed = false;

        if (existing.Title != shopifyVariant.Title)
        {
            existing.Title = shopifyVariant.Title;
            changed = true;
        }
        
        return changed;
    }
    
}
