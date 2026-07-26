namespace Infrastructure.Database.Entities;

/// <summary>
/// One Shopify listing belonging to a quarantined <see cref="SkulabsAmbiguousItemEntity"/>. When
/// <see cref="RawVariantId"/> matches a known variant, <see cref="ShopifyProductVariantId"/> is
/// populated (the variant may be active or deleted); it is null when we do not have that variant.
/// Internal SkuLabs (non-Shopify) listings are dropped before persistence and never appear here.
/// </summary>
public class SkulabsAmbiguousItemListingEntity
{
    public Guid SkulabsAmbiguousItemListingId { get; set; } = Guid.CreateVersion7();

    public Guid SkulabsAmbiguousItemId { get; set; }

    /// <summary>The SkuLabs <c>_id</c> of the listing.</summary>
    public string SkulabsSourceListingId { get; set; } = string.Empty;

    /// <summary>The Shopify variant id exactly as SkuLabs reported it.</summary>
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
