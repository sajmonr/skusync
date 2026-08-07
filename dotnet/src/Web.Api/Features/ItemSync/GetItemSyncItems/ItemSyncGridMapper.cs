using Gridify;
using Infrastructure.Database.Entities;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public static class ItemSyncGridMapper
{
    public static IGridifyMapper<VariantWithSkulabsItem> Instance { get; } =
        new GridifyMapper<VariantWithSkulabsItem>()
            .AddMap("id", entity => entity.Variant.ShopifyProductVariantId)
            .AddMap("displayName", entity => entity.Variant.DisplayName)
            .AddMap("shopifyId", entity => entity.Variant.VariantId)
            .AddMap("sku", entity => entity.Variant.Sku)
            .AddMap("barcode", entity => entity.Variant.Barcode)
            .AddMap("pendingShopifySync", entity => entity.Variant.PendingShopifySync)
            .AddMap("skulabsId", entity => entity.SkulabsItem == null ? "" : entity.SkulabsItem.SkulabsSourceItemId)
            .AddMap("skulabsTitle", entity => entity.SkulabsItem == null ? "" : entity.SkulabsItem.Title)
            .AddMap("pendingSkulabsSync", entity => entity.SkulabsItem != null && entity.SkulabsItem.PendingSkulabsSync);
}
