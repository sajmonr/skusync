using System.Text.Json.Serialization;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

/// <summary>
/// One listing on an ambiguous SkuLabs item. A listing resolves to one of our Shopify variants only
/// when its variant id is numeric and matches a known variant; otherwise it is an internal SkuLabs
/// (non-Shopify) listing, or a Shopify variant we do not have.
/// </summary>
public readonly record struct AmbiguousItemListingDetails(
    string ListingId,
    string RawVariantId,
    string SkulabsProductId,
    bool ResolvedToShopifyVariant,
    [property: JsonIgnore] long ResolvedProductId,
    long? ResolvedVariantId,
    string? ResolvedDisplayName,
    string ShopifyUrl);
