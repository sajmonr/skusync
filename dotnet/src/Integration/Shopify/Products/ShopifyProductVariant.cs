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
