namespace Integration.Skulabs.Items;

/// <summary>
/// A single channel listing on a SkuLabs item, as returned by the API. The variant id is kept
/// as the raw string SkuLabs sent — it is only a Shopify variant when it parses as a number;
/// non-numeric values denote an internal SkuLabs (non-Shopify) listing.
/// </summary>
public readonly record struct SkulabsApiListing(
    string ListingId,
    string RawVariantId,
    string ShopifyProductId);
