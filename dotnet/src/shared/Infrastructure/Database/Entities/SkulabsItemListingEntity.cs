namespace Infrastructure.Database.Entities;

/// <summary>
/// One Shopify listing SkuLabs reports for a <see cref="SkulabsItemEntity"/> — the link between a
/// SkuLabs item and one of our Shopify variants. Deliberately an explicit entity rather than a plain
/// many-to-many join: <see cref="ShopifyProductVariantId"/> is nullable because SkuLabs regularly
/// reports a listing on a variant we have never ingested, and <see cref="RawVariantId"/> preserves
/// what it said in that case. That unresolved listing is often the very thing explaining why an item
/// is ambiguous, so it must survive rather than be dropped for want of a foreign key.
/// </summary>
public class SkulabsItemListingEntity
{
    public Guid SkulabsItemListingId { get; set; } = Guid.CreateVersion7();

    public Guid SkulabsItemId { get; set; }

    /// <summary>The SkuLabs <c>_id</c> of the listing.</summary>
    public string SkulabsSourceListingId { get; set; } = string.Empty;

    /// <summary>The Shopify variant id exactly as SkuLabs reported it.</summary>
    public string RawVariantId { get; set; } = string.Empty;

    /// <summary>The Shopify product id SkuLabs reported for this listing (its <c>item_id</c>).</summary>
    public string ShopifyProductId { get; set; } = string.Empty;

    /// <summary>
    /// The resolved local Shopify variant, when <see cref="RawVariantId"/> matches a variant we hold.
    /// Null when we do not have that variant.
    /// </summary>
    public Guid? ShopifyProductVariantId { get; set; }

    public SkulabsItemEntity? SkulabsItem { get; set; }

    public ShopifyProductVariantEntity? ShopifyProductVariant { get; set; }
}
