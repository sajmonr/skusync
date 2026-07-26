using System.Text.Json.Serialization;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

/// <summary>
/// One Shopify listing on an ambiguous SkuLabs item. A listing resolves to one of our Shopify
/// variants when its variant id matches a known variant; if it does not resolve, it points at a
/// Shopify variant we do not have. <see cref="ResolvedVariantIsDeleted"/> flags a listing that
/// resolves to a variant that has since been deleted in Shopify — the reviewer should remove that
/// listing in SkuLabs.
/// </summary>
public readonly record struct AmbiguousItemListingDetails(
    string ListingId,
    string RawVariantId,
    string SkulabsProductId,
    bool ResolvedToShopifyVariant,
    bool ResolvedVariantIsDeleted,
    [property: JsonIgnore] long ResolvedProductId,
    long? ResolvedVariantId,
    string? ResolvedDisplayName,
    string ShopifyUrl);
