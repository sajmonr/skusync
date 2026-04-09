namespace Application;

/// <summary>
/// Defines the names of all feature flags used by the Application layer.
/// Use these constants everywhere — never write the string literal directly.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// When disabled, all writes back to Shopify (variant SKU/barcode sync) are skipped.
    /// </summary>
    public const string ShopifyWriteBack = "ShopifyWriteBack";
}
