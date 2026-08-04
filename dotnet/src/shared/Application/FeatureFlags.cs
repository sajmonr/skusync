namespace Application;

/// <summary>
/// Defines the names of all feature flags used by the Application layer.
/// Use these constants everywhere — never write the string literal directly.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Kill switch for every Shopify write — automatic and manual alike. Checked only inside
    /// <c>ShopifyDispatcher</c>'s push path; when disabled, dirty variants stay pending and
    /// nothing reaches Shopify.
    /// </summary>
    public const string ShopifyWriteBack = "ShopifyWriteBack";

    /// <summary>
    /// Kill switch for every SkuLabs write — automatic and manual alike. Checked only inside
    /// <c>SkulabsDispatcher</c>'s push path; when disabled, dirty items stay pending and
    /// nothing reaches SkuLabs.
    /// </summary>
    public const string SkulabsWriteBack = "SkulabsWriteBack";

    /// <summary>
    /// When disabled, the <em>automatic</em> Shopify dispatch triggers — the scheduled
    /// <c>shopify-dispatch</c> job and the immediate post-commit dispatch after SKU generation —
    /// no-op. Dirty variants accumulate as pending (visible in the Item Sync grid) until a manual
    /// sync pushes them or the flag is re-enabled. Defaults to enabled.
    /// </summary>
    public const string ShopifyAutoDispatch = "ShopifyAutoDispatch";

    /// <summary>
    /// When disabled, the <em>automatic</em> SkuLabs dispatch trigger — the scheduled
    /// <c>skulabs-dispatch</c> job — no-ops. Dirty items accumulate as pending until a manual
    /// sync pushes them or the flag is re-enabled. Defaults to enabled.
    /// </summary>
    public const string SkulabsAutoDispatch = "SkulabsAutoDispatch";

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
