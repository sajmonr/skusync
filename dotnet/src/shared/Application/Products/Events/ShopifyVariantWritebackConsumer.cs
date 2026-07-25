using Infrastructure.Database;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using SlimMessageBus;

namespace Application.Products.Events;

/// <summary>
/// Pushes a variant's current SKU/barcode from our database up to Shopify whenever the variant
/// changes. Variant-created and variant-updated are two triggers for the same reconciliation — it
/// reads the variant's present state and writes it back regardless of which happened — so both
/// funnel through one handler. The outbound write is gated by the <c>ShopifyWriteBack</c> flag.
/// </summary>
public class ShopifyVariantWritebackConsumer(
    ApplicationDbContext dbContext,
    IShopifyProductService shopifyProductService,
    IFeatureManager featureManager,
    ILogger<ShopifyVariantWritebackConsumer> logger)
    : IConsumer<ProductVariantCreatedEvent>,
        IConsumer<ProductVariantUpdatedEvent>
{
    public Task OnHandle(ProductVariantCreatedEvent message, CancellationToken cancellationToken) =>
        WriteBack(message.ProductVariantId, cancellationToken);

    public Task OnHandle(ProductVariantUpdatedEvent message, CancellationToken cancellationToken) =>
        WriteBack(message.ProductVariantId, cancellationToken);

    private async Task WriteBack(Guid productVariantId, CancellationToken cancellationToken)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack))
        {
            logger.LogInformation(
                "ShopifyWriteBack feature flag is disabled. Skipping Shopify update for variant {VariantId}.",
                productVariantId);
            return;
        }

        var variant = await dbContext.ShopifyProductVariants
            .Where(v => v.ShopifyProductVariantId == productVariantId)
            .Select(v => new { v.GlobalProductId, v.GlobalVariantId, v.Sku, v.Barcode })
            .FirstOrDefaultAsync(cancellationToken);

        if (variant is null)
        {
            logger.LogWarning(
                "Variant {VariantId} not found in the database. Skipping Shopify update.",
                productVariantId);
            return;
        }

        await shopifyProductService.UpdateVariants(variant.GlobalProductId,
            [new ShopifyUpdateProductVariant(variant.GlobalVariantId, variant.Sku, variant.Barcode)]);
    }
}
