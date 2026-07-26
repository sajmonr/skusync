namespace Infrastructure.Database.Entities;

/// <summary>
/// One channel listing belonging to a quarantined <see cref="SkulabsAmbiguousItemEntity"/>. A listing
/// may or may not point at one of our Shopify variants: when <see cref="RawVariantId"/> is numeric and
/// matches a known variant, <see cref="ShopifyProductVariantId"/> is populated; otherwise it is null
/// (an internal SkuLabs / non-Shopify listing, or a Shopify variant we do not have).
/// </summary>
public class SkulabsAmbiguousItemListingEntity
{
    public Guid SkulabsAmbiguousItemListingId { get; set; } = Guid.CreateVersion7();

    public Guid SkulabsAmbiguousItemId { get; set; }

    /// <summary>The SkuLabs <c>_id</c> of the listing.</summary>
    public string SkulabsSourceListingId { get; set; } = string.Empty;

    /// <summary>The variant id exactly as SkuLabs reported it — numeric for Shopify, otherwise not.</summary>
    public string RawVariantId { get; set; } = string.Empty;

    /// <summary>The Shopify product id SkuLabs reported for this listing (its <c>item_id</c>).</summary>
    public string ShopifyProductId { get; set; } = string.Empty;

    /// <summary>
    /// The resolved local Shopify variant, when <see cref="RawVariantId"/> is numeric and matches a
    /// known variant. Null for non-Shopify listings or variants we do not have.
    /// </summary>
    public Guid? ShopifyProductVariantId { get; set; }

    public SkulabsAmbiguousItemEntity? AmbiguousItem { get; set; }

    public ShopifyProductVariantEntity? ShopifyProductVariant { get; set; }
}
