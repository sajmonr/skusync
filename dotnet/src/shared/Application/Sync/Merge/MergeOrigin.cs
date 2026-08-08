namespace Application.Sync.Merge;

/// <summary>
/// What caused this merge to run. Rules need it because two of the codebase's field-authority rules
/// are deliberately different depending on how a variant reached us, and that difference is a
/// business decision rather than an inconsistency — see the SKU and barcode rules.
/// </summary>
public enum MergeOrigin
{
    /// <summary>
    /// A variant seen for the first time on a Shopify <c>products/create</c> or
    /// <c>products/update</c> webhook. Codes supplied in the payload are treated as suspect: the
    /// common way a variant appears this way is a merchant duplicating a product without clearing
    /// the original's SKU and barcode.
    /// </summary>
    WebhookCreate,

    /// <summary>
    /// A variant discovered by the catalogue import. Codes in the payload are honoured, because a
    /// SKU re-derived now cannot be matched against whatever was generated when the variant was
    /// first created — the product may since have been renamed, and the SKU derives from the name.
    /// </summary>
    Import,

    /// <summary>
    /// A variant we already track, being re-examined: the scheduled sweep, a SkuLabs item sync, or
    /// a manual sync. Nothing is originated here; existing decisions stand unless an external
    /// system has since asserted something that outranks them.
    /// </summary>
    Routine
}
