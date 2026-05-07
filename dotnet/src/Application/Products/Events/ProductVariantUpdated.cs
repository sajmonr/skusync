using Infrastructure.Database;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using SlimMessageBus;

namespace Application.Products.Events;

public readonly record struct ProductVariantUpdatedEvent(Guid ProductVariantId);

public class ProductVariantUpdatedConsumer(
    ApplicationDbContext dbContext,
    IShopifyProductService shopifyProductService,
    IFeatureManager featureManager,
    ILogger<ProductVariantUpdatedConsumer> logger) : IConsumer<ProductVariantUpdatedEvent>
{
    public async Task OnHandle(ProductVariantUpdatedEvent message, CancellationToken cancellationToken)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack))
        {
            logger.LogInformation(
                "ShopifyWriteBack feature flag is disabled. Skipping Shopify update for updated variant {VariantId}.",
                message.ProductVariantId);
            return;
        }

        var variant = await dbContext.ShopifyProductVariants
            .Where(v => v.ShopifyProductVariantId == message.ProductVariantId)
            .Select(v => new { v.GlobalProductId, v.GlobalVariantId, v.Sku, v.Barcode })
            .FirstOrDefaultAsync(cancellationToken);

        if (variant is null)
        {
            logger.LogWarning(
                "Variant {VariantId} not found in the database. Skipping Shopify update.",
                message.ProductVariantId);
            return;
        }

        await shopifyProductService.UpdateVariants(variant.GlobalProductId,
            [new ShopifyUpdateProductVariant(variant.GlobalVariantId, variant.Sku, variant.Barcode)]);
    }
}
