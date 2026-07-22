namespace Integration.Shopify.Products;

/// <summary>
/// Builds the canonical display name for a Shopify variant from the product and variant
/// titles. Shopify's own <c>displayName</c> field formats this as <c>"Title - Variant"</c>,
/// which is not what we store — we use <c>"Title (Variant)"</c>, and just the product
/// title for option-less products (variant title is "Default Title").
/// </summary>
public static class ShopifyDisplayName
{
    private const string DefaultVariantTitle = "Default Title";

    public static string Compose(string productTitle, string variantTitle)
    {
        return string.IsNullOrEmpty(variantTitle) || variantTitle == DefaultVariantTitle
            ? productTitle
            : $"{productTitle} ({variantTitle})";
    }
}
