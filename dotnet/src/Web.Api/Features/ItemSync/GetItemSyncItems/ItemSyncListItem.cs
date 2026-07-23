using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Infrastructure.Database.Entities;
using SharedKernel;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public readonly record struct ItemSyncListItem(
    Guid Id,
    string DisplayName,
    [property: JsonIgnore] long ShopifyProductId,
    long ShopifyId,
    string Sku,
    string Barcode,
    bool PendingShopifySync,
    string ShopifyUrl,
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
            "",
            entity.SkulabsItem == null
                ? null
                : new SkulabsItemSyncDetails(
                    entity.SkulabsItem.SkulabsSourceItemId,
                    entity.SkulabsItem.Title,
                    entity.SkulabsItem.Sku,
                    entity.SkulabsItem.Barcode,
                    entity.SkulabsItem.PendingSkulabsSync,
                    ""));

    public ItemSyncListItem WithExternalUrls()
    {
        SkulabsItemSyncDetails? skulabs = Skulabs is { } item
            ? item with { Url = ExternalItemUrls.CreateSkulabsItemUrl(item.Id) }
            : null;

        return this with
        {
            ShopifyUrl = ExternalItemUrls.CreateShopifyProductUrl(ShopifyProductId, ShopifyId),
            Skulabs = skulabs
        };
    }
}
