using Infrastructure.Database.Entities;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public static class AmbiguousItemsQueryExtensions
{
    /// <summary>
    /// An item is ambiguous when SkuLabs reports more than one Shopify listing for it, so there is no
    /// single variant to sync it against. Derived on read — nothing marks an item as quarantined.
    /// </summary>
    public static IQueryable<SkulabsItemEntity> WhereAmbiguous(this IQueryable<SkulabsItemEntity> query) =>
        query.Where(entity => entity.Listings.Count > 1);

    public static IQueryable<SkulabsItemEntity> ApplyAmbiguousItemsSearch(
        this IQueryable<SkulabsItemEntity> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var normalizedSearch = search.Trim().ToLower();

        return query.Where(entity =>
            entity.Title.ToLower().Contains(normalizedSearch) ||
            entity.Sku.ToLower().Contains(normalizedSearch) ||
            entity.Barcode.ToLower().Contains(normalizedSearch) ||
            entity.SkulabsSourceItemId.ToLower().Contains(normalizedSearch) ||
            entity.Listings.Any(listing =>
                listing.RawVariantId.ToLower().Contains(normalizedSearch) ||
                listing.ShopifyProductId.ToLower().Contains(normalizedSearch)));
    }
}
