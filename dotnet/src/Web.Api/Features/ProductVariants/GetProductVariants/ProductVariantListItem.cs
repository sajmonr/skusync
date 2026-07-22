using System.Linq.Expressions;
using Infrastructure.Database.Entities;

namespace Web.Api.Features.ProductVariants.GetProductVariants;

public readonly record struct ProductVariantListItem(
    Guid Id,
    long ProductId,
    long VariantId,
    string DisplayName,
    string Sku,
    string Barcode,
    bool PendingSync,
    int FailedSyncAttempts,
    bool Active,
    DateTime UpdatedOnUtc)
{
    public static readonly Expression<Func<ShopifyProductVariantEntity, ProductVariantListItem>> Projection =
        entity => new ProductVariantListItem(
            entity.ShopifyProductVariantId,
            entity.ProductId,
            entity.VariantId,
            entity.DisplayName,
            entity.Sku,
            entity.Barcode,
            entity.PendingShopifySync,
            entity.FailedShopifySyncAttempts,
            entity.IsActive,
            entity.UpdatedOnUtc);
}
