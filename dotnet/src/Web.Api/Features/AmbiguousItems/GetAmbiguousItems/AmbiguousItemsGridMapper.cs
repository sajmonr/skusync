using Gridify;
using Infrastructure.Database.Entities;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public static class AmbiguousItemsGridMapper
{
    public static IGridifyMapper<SkulabsAmbiguousItemEntity> Instance { get; } =
        new GridifyMapper<SkulabsAmbiguousItemEntity>()
            .AddMap("id", entity => entity.SkulabsAmbiguousItemId)
            .AddMap("skulabsItemId", entity => entity.SkulabsSourceItemId)
            .AddMap("name", entity => entity.Name)
            .AddMap("sku", entity => entity.Sku)
            .AddMap("upc", entity => entity.Upc)
            .AddMap("listingCount", entity => entity.ListingCount)
            .AddMap("reason", entity => entity.ReasonNavigation!.Name)
            .AddMap("status", entity => entity.StatusNavigation!.Name)
            .AddMap("firstSeenUtc", entity => entity.FirstSeenUtc)
            .AddMap("lastSeenUtc", entity => entity.LastSeenUtc);
}
