using Infrastructure.Database.Entities;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public static class AmbiguousItemsQueryExtensions
{
    public static IQueryable<SkulabsAmbiguousItemEntity> ApplyAmbiguousItemsSearch(
        this IQueryable<SkulabsAmbiguousItemEntity> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var normalizedSearch = search.Trim().ToLower();

        return query.Where(entity =>
            entity.Name.ToLower().Contains(normalizedSearch) ||
            entity.Sku.ToLower().Contains(normalizedSearch) ||
            entity.Upc.ToLower().Contains(normalizedSearch) ||
            entity.SkulabsSourceItemId.ToLower().Contains(normalizedSearch) ||
            entity.Listings.Any(listing =>
                listing.RawVariantId.ToLower().Contains(normalizedSearch) ||
                listing.ShopifyProductId.ToLower().Contains(normalizedSearch)));
    }
}
