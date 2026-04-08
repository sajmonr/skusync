namespace Integration.Shopify.Products;

/// <summary>
/// Represents a single Shopify product variant returned from the Shopify API.
/// Numeric IDs are parsed from the Shopify Admin GraphQL global IDs on construction.
/// </summary>
public readonly record struct ShopifyProductVariant(
    /// <summary>The Shopify Admin GraphQL global product ID, e.g. <c>gid://shopify/Product/123</c>.</summary>
    string GlobalProductId,
    /// <summary>The Shopify Admin GraphQL global variant ID, e.g. <c>gid://shopify/ProductVariant/456</c>.</summary>
    string GlobalVariantId,
    /// <summary>The display title of the parent product.</summary>
    string ProductTitle,
    /// <summary>
    /// The display title of this specific variant (e.g. "Large / Blue").
    /// Empty string when Shopify reports the default single-variant title.
    /// </summary>
    string VariantTitle,
    /// <summary>The stock-keeping unit (SKU) assigned to this variant.</summary>
    string Sku,
    /// <summary>The barcode (EAN/UPC) assigned to this variant.</summary>
    string Barcode)
{
    /// <summary>
    /// Gets the numeric Shopify product ID parsed from the trailing segment of <see cref="GlobalProductId"/>.
    /// Returns <c>0</c> when the ID cannot be parsed.
    /// </summary>
    public long ProductId { get; } = GetIdOrDefault(GlobalProductId);

    /// <summary>
    /// Gets the numeric Shopify variant ID parsed from the trailing segment of <see cref="GlobalVariantId"/>.
    /// Returns <c>0</c> when the ID cannot be parsed.
    /// </summary>
    public long VariantId { get; } = GetIdOrDefault(GlobalVariantId);

    private static long GetIdOrDefault(string id)
    {
        if (long.TryParse(id[(id.LastIndexOf('/') + 1)..], out var longId))
        {
            return longId;
        }

        return 0;
    }
}
