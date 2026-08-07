using Infrastructure.Database.Entities;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public static class ItemSyncQueryExtensions
{
    public static IQueryable<VariantWithSkulabsItem> ApplyItemSyncSearch(
        this IQueryable<VariantWithSkulabsItem> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var normalizedSearch = search.Trim().ToLower();

        return query.Where(entity =>
            entity.Variant.DisplayName.ToLower().Contains(normalizedSearch) ||
            entity.Variant.ProductId.ToString().Contains(normalizedSearch) ||
            entity.Variant.VariantId.ToString().Contains(normalizedSearch) ||
            entity.Variant.Sku.ToLower().Contains(normalizedSearch) ||
            entity.Variant.Barcode.ToLower().Contains(normalizedSearch) ||
            (entity.SkulabsItem != null &&
                (entity.SkulabsItem.SkulabsSourceItemId.ToLower().Contains(normalizedSearch) ||
                 entity.SkulabsItem.Title.ToLower().Contains(normalizedSearch) ||
                 entity.SkulabsItem.Sku.ToLower().Contains(normalizedSearch) ||
                 entity.SkulabsItem.Barcode.ToLower().Contains(normalizedSearch))));
    }

    /// <summary>
    /// <c>missing-in-skulabs</c> covers a variant with no SkuLabs listing at all and one whose only
    /// listing belongs to an ambiguous item alike: in both cases there is no item we can act on, which
    /// is what the filter means.
    /// </summary>
    public static IQueryable<VariantWithSkulabsItem> ApplyItemSyncStatusFilter(
        this IQueryable<VariantWithSkulabsItem> query,
        string? status) => status switch
        {
            "pending-sync" => query.Where(entity =>
                entity.Variant.PendingShopifySync ||
                (entity.SkulabsItem != null && entity.SkulabsItem.PendingSkulabsSync)),
            "missing-in-skulabs" => query.Where(entity => entity.SkulabsItem == null),
            "out-of-sync" => query.Where(entity =>
                !entity.Variant.PendingShopifySync &&
                entity.SkulabsItem != null &&
                !entity.SkulabsItem.PendingSkulabsSync &&
                entity.Variant.DisplayName != entity.SkulabsItem.Title),
            "in-sync" => query.Where(entity =>
                !entity.Variant.PendingShopifySync &&
                entity.SkulabsItem != null &&
                !entity.SkulabsItem.PendingSkulabsSync &&
                entity.Variant.DisplayName == entity.SkulabsItem.Title),
            _ => query
        };
}
