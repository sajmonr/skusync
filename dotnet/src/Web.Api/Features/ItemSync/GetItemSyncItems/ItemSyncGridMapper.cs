using Gridify;
using Infrastructure.Database.Entities;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public static class ItemSyncGridMapper
{
    public static IGridifyMapper<ShopifyProductVariantEntity> Instance { get; } =
        new GridifyMapper<ShopifyProductVariantEntity>()
            .AddMap("id", entity => entity.ShopifyProductVariantId)
            .AddMap("displayName", entity => entity.DisplayName)
            .AddMap("shopifyProductId", entity => entity.ProductId)
            .AddMap("shopifyVariantId", entity => entity.VariantId)
            .AddMap("sku", entity => entity.Sku)
            .AddMap("barcode", entity => entity.Barcode)
            .AddMap("pendingShopifySync", entity => entity.PendingShopifySync)
            .AddMap("skulabsId", entity => entity.SkulabsItem == null ? "" : entity.SkulabsItem.SkulabsSourceItemId)
            .AddMap("skulabsTitle", entity => entity.SkulabsItem == null ? "" : entity.SkulabsItem.Title)
            .AddMap("pendingSkulabsSync", entity => entity.SkulabsItem != null && entity.SkulabsItem.PendingSkulabsSync);
}
