using System.Linq.Expressions;
using Infrastructure.Database.Entities;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public readonly record struct ItemSyncListItem(
    Guid Id,
    string DisplayName,
    long ShopifyProductId,
    long ShopifyVariantId,
    string Sku,
    string Barcode,
    bool PendingShopifySync,
    SkulabsItemSyncDetails? Skulabs)
{
    public static readonly Expression<Func<ShopifyProductVariantEntity, ItemSyncListItem>> Projection =
        entity => new ItemSyncListItem(
            entity.ShopifyProductVariantId,
            entity.DisplayName,
            entity.ProductId,
            entity.VariantId,
            entity.Sku,
            entity.Barcode,
            entity.PendingShopifySync,
            entity.SkulabsItem == null
                ? null
                : new SkulabsItemSyncDetails(
                    entity.SkulabsItem.SkulabsSourceItemId,
                    entity.SkulabsItem.Title,
                    entity.SkulabsItem.Sku,
                    entity.SkulabsItem.Barcode,
                    entity.SkulabsItem.PendingSkulabsSync));
}
