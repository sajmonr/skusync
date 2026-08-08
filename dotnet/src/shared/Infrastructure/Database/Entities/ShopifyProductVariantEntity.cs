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
    /// The product's own title, as Shopify sent it.
    /// </summary>
    /// <remarks>
    /// Kept alongside <see cref="DisplayName"/> rather than derived from it. SKU generation reads
    /// the product and variant titles separately — it abbreviates each — and a composed
    /// "Product (Variant)" string cannot be split back apart reliably, since either part may itself
    /// contain brackets. Storing the raw values keeps generated SKUs the shape merchants recognise.
    /// </remarks>
    public string ProductTitle { get; set; } = "";

    /// <summary>
    /// The variant's own title, as Shopify sent it (e.g. <c>Large / Black</c>), or empty for a
    /// product with no options.
    /// </summary>
    public string VariantTitle { get; set; } = "";

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

    /// <summary>
    /// Gets or sets a value indicating whether the underlying Shopify variant no longer exists —
    /// it was absent from an authoritative <c>products/update</c> payload (e.g. Shopify removed a
    /// product's standalone default variant when real variants were created). This is
    /// <b>terminal</b>: once set to <c>true</c> the row is never revived. A Shopify variant that
    /// "returns" does so under a new variant id and is created as a fresh row. Deleted rows are
    /// preserved (never physically removed) so their history and audit log survive, and are
    /// excluded from all sync work and the active item-sync list.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp at which <see cref="IsDeleted"/> was flipped to
    /// <c>true</c>. Defaults to <see cref="DateTime.MinValue"/> for rows that are not deleted.
    /// </summary>
    public DateTime DeletedOn { get; set; } = DateTime.MinValue;

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

    /// <summary>
    /// Every SkuLabs listing pointing at this variant. Normally zero or one; more than one means two
    /// SkuLabs items claim the same variant, which makes the link unusable rather than merely
    /// ambiguous — see <see cref="SkulabsItemLinks.IsSyncable"/>.
    /// </summary>
    public ICollection<SkulabsItemListingEntity> SkulabsItemListings { get; set; } =
        new HashSet<SkulabsItemListingEntity>();

    /// <summary>
    /// What this variant and its linked SkuLabs item should hold, as decided by the reconciler.
    /// The columns on this entity are the Shopify <em>mirror</em> — what Shopify last told us — so
    /// a difference between the two is precisely what "pending a push" means.
    /// <para>
    /// Null only in the window between a variant being ingested and the next reconcile pass
    /// creating its state; every read path must tolerate that rather than assume it away.
    /// </para>
    /// </summary>
    public DesiredItemStateEntity? DesiredState { get; set; }
}
