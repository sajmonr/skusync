namespace Web.Api.Shopify;

/// <summary>
/// Reads numeric Shopify IDs out of the Admin GraphQL global IDs that Shopify surfaces hand to
/// extensions, so callers can pass a resource through untouched rather than parsing it client-side.
/// </summary>
public static class ShopifyGlobalId
{
    private const string ProductPrefix = "gid://shopify/Product/";
    private const string ProductVariantPrefix = "gid://shopify/ProductVariant/";

    /// <summary>
    /// Parses a product variant ID supplied either as an Admin GraphQL global ID
    /// (<c>gid://shopify/ProductVariant/987654321</c>) or as the bare numeric ID.
    /// </summary>
    /// <param name="value">The value to parse.</param>
    /// <param name="variantId">The numeric variant ID when parsing succeeds; otherwise zero.</param>
    /// <returns><c>true</c> when <paramref name="value"/> yields a positive variant ID.</returns>
    public static bool TryParseVariantId(string? value, out long variantId) =>
        TryParseId(value, ProductVariantPrefix, out variantId);

    /// <summary>
    /// Parses a product ID supplied either as an Admin GraphQL global ID
    /// (<c>gid://shopify/Product/987654321</c>) or as the bare numeric ID.
    /// </summary>
    /// <param name="value">The value to parse.</param>
    /// <param name="productId">The numeric product ID when parsing succeeds; otherwise zero.</param>
    /// <returns><c>true</c> when <paramref name="value"/> yields a positive product ID.</returns>
    public static bool TryParseProductId(string? value, out long productId) =>
        TryParseId(value, ProductPrefix, out productId);

    /// <summary>
    /// Strips <paramref name="prefix"/> when present and parses what is left. A global ID naming a
    /// different resource type never matches the prefix, so it falls through to the numeric parse and
    /// is rejected there — a variant ID cannot be smuggled in where a product ID is expected.
    /// </summary>
    private static bool TryParseId(string? value, string prefix, out long id)
    {
        id = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var numericPart = trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;

        return long.TryParse(numericPart, out id) && id > 0;
    }
}
