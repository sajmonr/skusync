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

    /// <summary>
    /// When disabled, Shopify product webhook handlers (products/create, products/update)
    /// return immediately without persisting anything or publishing events.
    /// Defaults to enabled (configured in appsettings).
    /// </summary>
    public const string ShopifySyncEnabled = "ShopifySyncEnabled";

    /// <summary>
    /// When disabled, the scheduled SkuLabs item sync job returns immediately without
    /// fetching from SkuLabs or touching the database.
    /// Defaults to disabled (omit from appsettings, or set explicitly to false).
    /// </summary>
    public const string SkulabsSyncEnabled = "SkulabsSyncEnabled";
}
