namespace Integration.Shopify.Products;

/// <summary>
/// Represents a single Shopify product variant returned from the Shopify API.
/// Numeric IDs are parsed from the Shopify Admin GraphQL global IDs on construction.
/// </summary>
public readonly record struct ShopifyProductVariant(
    string GlobalProductId,
    string GlobalVariantId,
    string DisplayName,
    string Sku,
    string Barcode)
{
    /// <summary>
    /// Gets the Shopify product's title (e.g. "Basic Tee"). Defaults to <see cref="DisplayName"/>
    /// when the caller did not supply a title separately — sufficient for downstream uses that
    /// only need a non-empty product name.
    /// </summary>
    public string ProductTitle { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Shopify variant's title (e.g. "Large / Black", or "Default Title" for products
    /// without options). Empty when the caller did not supply one.
    /// </summary>
    public string VariantTitle { get; init; } = string.Empty;

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
