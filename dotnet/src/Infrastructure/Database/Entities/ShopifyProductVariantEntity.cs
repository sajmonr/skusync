namespace Infrastructure.Database.Entities;

/// <summary>
/// Represents a single Shopify product variant persisted in the local database.
/// Each row corresponds to one variant (size, colour, etc.) belonging to a Shopify product.
/// </summary>
public class ShopifyProductVariantEntity
{
    private string _fullTitle = "";

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

    /// <summary>
    /// Gets or sets the product title as shown in Shopify. Setting this value also
    /// regenerates <see cref="FullTitle"/>.
    /// </summary>
    public string ProductTitle
    {
        get;
        set
        {
            field = value;
            GenerateFullTitle();
        }
    } = "";

    /// <summary>
    /// Gets or sets the variant title (e.g. "Large / Blue"). Shopify's default variant
    /// title <c>"Default Title"</c> is normalised to an empty string. Setting this value
    /// also regenerates <see cref="FullTitle"/>.
    /// </summary>
    public string VariantTitle
    {
        get;
        set
        {
            if (value == "Default Title")
            {
                field = "";
                return;
            }

            field = value;
            GenerateFullTitle();
        }
    } = "";

    /// <summary>
    /// Gets the combined display title in the form <c>"{ProductTitle} ({VariantTitle})"</c>,
    /// or just the product title when the variant title is empty.
    /// This property is computed from <see cref="ProductTitle"/> and <see cref="VariantTitle"/>
    /// and is stored as a denormalised column for efficient querying.
    /// </summary>
    public string FullTitle
    {
        get => _fullTitle;
        init => _fullTitle = value;
    }

    /// <summary>Gets or sets the stock-keeping unit (SKU) assigned to this variant.</summary>
    public string Sku { get; set; } = "";

    /// <summary>Gets or sets the barcode (EAN/UPC) assigned to this variant.</summary>
    public string Barcode { get; set; } = "";

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
    
    private void GenerateFullTitle()
    {
        _fullTitle = string.IsNullOrWhiteSpace(VariantTitle) ? ProductTitle : $"{ProductTitle} ({VariantTitle})";
    }
}
