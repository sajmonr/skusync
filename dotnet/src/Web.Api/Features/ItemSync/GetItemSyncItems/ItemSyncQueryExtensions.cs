using Infrastructure.Database.Entities;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public static class ItemSyncQueryExtensions
{
    public static IQueryable<ShopifyProductVariantEntity> ApplyItemSyncSearch(
        this IQueryable<ShopifyProductVariantEntity> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var normalizedSearch = search.Trim().ToLower();

        return query.Where(entity =>
            entity.DisplayName.ToLower().Contains(normalizedSearch) ||
            entity.ProductId.ToString().Contains(normalizedSearch) ||
            entity.VariantId.ToString().Contains(normalizedSearch) ||
            entity.Sku.ToLower().Contains(normalizedSearch) ||
            entity.Barcode.ToLower().Contains(normalizedSearch) ||
            (entity.SkulabsItem != null &&
                (entity.SkulabsItem.SkulabsSourceItemId.ToLower().Contains(normalizedSearch) ||
                 entity.SkulabsItem.Title.ToLower().Contains(normalizedSearch) ||
                 entity.SkulabsItem.Sku.ToLower().Contains(normalizedSearch) ||
                 entity.SkulabsItem.Barcode.ToLower().Contains(normalizedSearch))));
    }

    public static IQueryable<ShopifyProductVariantEntity> ApplyItemSyncStatusFilter(
        this IQueryable<ShopifyProductVariantEntity> query,
        string? status) => status switch
        {
            "pending-sync" => query.Where(entity =>
                entity.PendingShopifySync ||
                (entity.SkulabsItem != null && entity.SkulabsItem.PendingSkulabsSync)),
            "missing-in-skulabs" => query.Where(entity => entity.SkulabsItem == null),
            "out-of-sync" => query.Where(entity =>
                !entity.PendingShopifySync &&
                entity.SkulabsItem != null &&
                !entity.SkulabsItem.PendingSkulabsSync &&
                entity.DisplayName != entity.SkulabsItem.Title),
            "in-sync" => query.Where(entity =>
                !entity.PendingShopifySync &&
                entity.SkulabsItem != null &&
                !entity.SkulabsItem.PendingSkulabsSync &&
                entity.DisplayName == entity.SkulabsItem.Title),
            _ => query
        };
}
