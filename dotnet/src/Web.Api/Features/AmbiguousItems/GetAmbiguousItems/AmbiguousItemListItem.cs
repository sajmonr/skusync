using System.Linq.Expressions;
using Infrastructure.Database.Entities;
using SharedKernel;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

/// <summary>
/// A quarantined SkuLabs item and every listing that made it ambiguous, shaped for the dashboard grid.
/// </summary>
public readonly record struct AmbiguousItemListItem(
    Guid Id,
    string SkulabsItemId,
    string Name,
    string Sku,
    string Upc,
    int ListingCount,
    string Reason,
    string Status,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string SkulabsUrl,
    IReadOnlyList<AmbiguousItemListingDetails> Listings)
{
    public static readonly Expression<Func<SkulabsAmbiguousItemEntity, AmbiguousItemListItem>> Projection =
        entity => new AmbiguousItemListItem(
            entity.SkulabsAmbiguousItemId,
            entity.SkulabsSourceItemId,
            entity.Name,
            entity.Sku,
            entity.Upc,
            entity.ListingCount,
            entity.ReasonNavigation!.Name,
            entity.StatusNavigation!.Name,
            entity.FirstSeenUtc,
            entity.LastSeenUtc,
            "",
            entity.Listings
                .Select(listing => new AmbiguousItemListingDetails(
                    listing.SkulabsSourceListingId,
                    listing.RawVariantId,
                    listing.ShopifyProductId,
                    listing.ShopifyProductVariant != null,
                    listing.ShopifyProductVariant == null ? 0 : listing.ShopifyProductVariant.ProductId,
                    listing.ShopifyProductVariant == null ? (long?)null : listing.ShopifyProductVariant.VariantId,
                    listing.ShopifyProductVariant == null ? null : listing.ShopifyProductVariant.DisplayName,
                    ""))
                .ToList());

    public AmbiguousItemListItem WithExternalUrls()
    {
        var listings = Listings
            .Select(listing => listing.ResolvedToShopifyVariant && listing.ResolvedVariantId is { } variantId
                ? listing with { ShopifyUrl = ExternalItemUrls.CreateShopifyProductUrl(listing.ResolvedProductId, variantId) }
                : listing)
            .ToArray();

        return this with
        {
            SkulabsUrl = ExternalItemUrls.CreateSkulabsItemUrl(SkulabsItemId),
            Listings = listings
        };
    }
}
