using Gridify;
using Infrastructure.Database.Entities;

namespace Web.Api.Features.ProductVariants.GetProductVariants;

public static class ProductVariantGridMapper
{
    public static IGridifyMapper<ShopifyProductVariantEntity> Instance { get; } =
        new GridifyMapper<ShopifyProductVariantEntity>()
            .AddMap("id", entity => entity.ShopifyProductVariantId)
            .AddMap("productId", entity => entity.ProductId)
            .AddMap("variantId", entity => entity.VariantId)
            .AddMap("displayName", entity => entity.DisplayName)
            .AddMap("sku", entity => entity.Sku)
            .AddMap("barcode", entity => entity.Barcode)
            .AddMap("pendingSync", entity => entity.PendingShopifySync)
            .AddMap("failedSyncAttempts", entity => entity.FailedShopifySyncAttempts)
            .AddMap("active", entity => entity.IsActive)
            .AddMap("updatedOnUtc", entity => entity.UpdatedOnUtc);
}
