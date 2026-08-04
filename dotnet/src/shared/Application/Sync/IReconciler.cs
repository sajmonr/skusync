namespace Application.Sync;

/// <summary>
/// The reconcile stage of the sync pipeline: compares the local mirrors of Shopify and SkuLabs
/// according to the field-authority rules and corrects the local rows, marking each corrected row
/// pending a push to its target system. Pure local computation — no feature flags, no external
/// calls; pushing is the dispatchers' job.
/// <list type="bullet">
///   <item><description>SkuLabs owns SKU and barcode (a blank SkuLabs value is never authoritative):
///   drifted values are mirrored into the variant, which is marked <c>PendingShopifySync</c>.</description></item>
///   <item><description>The variant <c>DisplayName</c> owns the SkuLabs item title: drifted titles are
///   mirrored into the item, which is marked <c>PendingSkulabsSync</c>.</description></item>
/// </list>
/// </summary>
public interface IReconciler
{
    /// <summary>Reconciles every linked variant/item pair. The nightly safety-net sweep.</summary>
    Task<ReconcileResult> ReconcileAll(CancellationToken cancellationToken = default);

    /// <summary>Reconciles the linked pairs for the given variants. Invoked inline by ingest after webhook changes.</summary>
    Task<ReconcileResult> ReconcileVariants(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the linked pairs for the given SkuLabs items. Invoked inline after the item sync links or re-links items.</summary>
    Task<ReconcileResult> ReconcileSkulabsItems(IReadOnlyCollection<Guid> skulabsItemIds, CancellationToken cancellationToken = default);
}
