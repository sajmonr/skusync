using Gridify;
using Infrastructure.Database.Entities;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public static class AmbiguousItemsGridMapper
{
    public static IGridifyMapper<SkulabsItemEntity> Instance { get; } =
        new GridifyMapper<SkulabsItemEntity>()
            .AddMap("id", entity => entity.SkulabsItemId)
            .AddMap("skulabsItemId", entity => entity.SkulabsSourceItemId)
            .AddMap("name", entity => entity.Title)
            .AddMap("sku", entity => entity.Sku)
            .AddMap("upc", entity => entity.Barcode)
            .AddMap("listingCount", entity => entity.Listings.Count)
            .AddMap("firstSeenUtc", entity => entity.FirstSeenUtc)
            .AddMap("lastSeenUtc", entity => entity.LastSeenUtc);
}
