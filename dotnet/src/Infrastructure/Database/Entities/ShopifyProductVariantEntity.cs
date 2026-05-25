namespace Infrastructure.Database.Entities;

/// <summary>
/// Represents a single Shopify product variant persisted in the local database.
/// Each row corresponds to one variant (size, colour, etc.) belonging to a Shopify product.
/// </summary>
public class ShopifyProductVariantEntity
{
    /// <summary>Gets or sets the surrogate primary key for this entity (UUIDv7).</summary>
    public Guid ShopifyProductVariantId { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Gets or sets the Shopify Admin GraphQL global product ID,
    /// e.g. <c>gid://shopify/Product/123456789</c>.
    /// </summary>
    public string GlobalProductId { get; set; } = "";

    /// <summary>Gets or sets the numeric Shopify product ID extracted from <see cref="GlobalProductId"/>.</summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Gets or sets the Shopify Admin GraphQL global variant ID,
    /// e.g. <c>gid://shopify/ProductVariant/987654321</c>.
    /// </summary>
    public string GlobalVariantId { get; set; } = "";

    /// <summary>Gets or sets the numeric Shopify variant ID extracted from <see cref="GlobalVariantId"/>.</summary>
    public long VariantId { get; set; }

    /// <summary>Gets or sets the stock-keeping unit (SKU) assigned to this variant.</summary>
    public string Sku { get; set; } = "";

    /// <summary>Gets or sets the barcode (EAN/UPC) assigned to this variant.</summary>
    public string Barcode { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether this variant has been locally corrected to
    /// match its linked SkuLabs item but the corresponding Shopify mutation hasn't yet
    /// succeeded — either because the <c>ShopifyWriteBack</c> feature flag was disabled at
    /// the time of the correction, or because the call was made but Shopify hasn't been
    /// reconciled with the new values. The periodic drift sync picks up variants in this
    /// state and pushes their <see cref="Sku"/>/<see cref="Barcode"/> to Shopify, clearing
    /// the flag on success.
    /// </summary>
    public bool PendingShopifySync { get; set; }

    /// <summary>
    /// Gets or sets the number of consecutive failed attempts to push the SkuLabs-authoritative
    /// SKU/barcode to Shopify for this variant. Reset to zero on a successful push. When this
    /// counter reaches the deactivation threshold, <see cref="IsActive"/> is flipped to
    /// <c>false</c> so the variant is excluded from future syncs and other queries.
    /// </summary>
    public int FailedShopifySyncAttempts { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this variant participates in any sync work or
    /// query. Defaults to <c>true</c>. Set to <c>false</c> when a Shopify push has failed enough
    /// consecutive times that the row is presumed dead (e.g. the underlying product was deleted
    /// in Shopify) and we should stop retrying it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the UTC timestamp at which this record was first created.</summary>
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the UTC timestamp at which this record was last modified.</summary>
    public DateTime UpdatedOnUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the collection of log events associated with this Shopify product variant.
    /// These log events capture historical data or changes related to the variant,
    /// such as updates, errors, or other relevant messages.
    /// </summary>
    public ICollection<ShopifyProductVariantLogEventEntity> LogEvents { get; set; } =
        new HashSet<ShopifyProductVariantLogEventEntity>();

    public SkulabsItemEntity? SkulabsItem { get; set; }
}
