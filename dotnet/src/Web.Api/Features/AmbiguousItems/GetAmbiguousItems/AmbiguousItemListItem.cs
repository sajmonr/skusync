using System.Linq.Expressions;
using Infrastructure.Database.Entities;
using SharedKernel;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

/// <summary>
/// A SkuLabs item carrying more than one Shopify listing, with every listing that made it ambiguous,
/// shaped for the dashboard grid. Ambiguity is derived from listing cardinality rather than stored, so
/// there is no quarantine row behind this — <see cref="Id"/> is the item's own identifier.
/// </summary>
public readonly record struct AmbiguousItemListItem(
    Guid Id,
    string SkulabsItemId,
    string Name,
    string Sku,
    string Upc,
    int ListingCount,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string SkulabsUrl,
    IReadOnlyList<AmbiguousItemListingDetails> Listings)
{
    public static readonly Expression<Func<SkulabsItemEntity, AmbiguousItemListItem>> Projection =
        entity => new AmbiguousItemListItem(
            entity.SkulabsItemId,
            entity.SkulabsSourceItemId,
            entity.Title,
            entity.Sku,
            entity.Barcode,
            entity.Listings.Count,
            entity.FirstSeenUtc,
            entity.LastSeenUtc,
            "",
            entity.Listings
                .Select(listing => new AmbiguousItemListingDetails(
                    listing.SkulabsSourceListingId,
                    listing.RawVariantId,
                    listing.ShopifyProductId,
                    listing.ShopifyProductVariant != null,
                    listing.ShopifyProductVariant != null && listing.ShopifyProductVariant.IsDeleted,
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
