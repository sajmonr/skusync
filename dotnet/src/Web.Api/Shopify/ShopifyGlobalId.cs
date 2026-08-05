namespace Web.Api.Shopify;

/// <summary>
/// Reads numeric Shopify IDs out of the Admin GraphQL global IDs that Shopify surfaces hand to
/// extensions, so callers can pass a variant through untouched rather than parsing it client-side.
/// </summary>
public static class ShopifyGlobalId
{
    private const string ProductVariantPrefix = "gid://shopify/ProductVariant/";

    /// <summary>
    /// Parses a product variant ID supplied either as an Admin GraphQL global ID
    /// (<c>gid://shopify/ProductVariant/987654321</c>) or as the bare numeric ID.
    /// </summary>
    /// <param name="value">The value to parse.</param>
    /// <param name="variantId">The numeric variant ID when parsing succeeds; otherwise zero.</param>
    /// <returns><c>true</c> when <paramref name="value"/> yields a positive variant ID.</returns>
    public static bool TryParseVariantId(string? value, out long variantId)
    {
        variantId = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var numericPart = trimmed.StartsWith(ProductVariantPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[ProductVariantPrefix.Length..]
            : trimmed;

        return long.TryParse(numericPart, out variantId) && variantId > 0;
    }
}
