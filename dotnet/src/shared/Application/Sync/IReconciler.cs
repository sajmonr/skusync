using Application.Sync.Merge;

namespace Application.Sync;

/// <summary>
/// The reconcile stage of the sync pipeline, and the only place field authority is decided.
/// <para>
/// Reads the two mirrors — what Shopify last told us, what SkuLabs last told us — runs the
/// registered <see cref="IMergeRule"/> chain over them, and writes the outcome to the variant's
/// desired state. Where the desired state then differs from a mirror, that mirror's system is owed
/// a push and the row is marked pending.
/// </para>
/// <para>
/// Pure local computation: no feature flags, no external calls. Ingest supplies the mirrors,
/// dispatchers do the pushing, and neither decides anything. The rules themselves document why each
/// field goes the way it does; the short version is that a value printed onto a physical label
/// outranks one that only exists in a database.
/// </para>
/// </summary>
public interface IReconciler
{
    /// <summary>Reconciles every variant. The nightly safety-net sweep.</summary>
    Task<ReconcileResult> ReconcileAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the given variants. Invoked inline by ingest, which passes the origin so the SKU
    /// and barcode rules can tell a first sighting from a routine re-examination.
    /// </summary>
    Task<ReconcileResult> ReconcileVariants(
        IReadOnlyCollection<Guid> variantIds,
        MergeOrigin origin = MergeOrigin.Routine,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles the variants linked to the given SkuLabs items. Invoked inline after an item sync.</summary>
    Task<ReconcileResult> ReconcileSkulabsItems(
        IReadOnlyCollection<Guid> skulabsItemIds,
        CancellationToken cancellationToken = default);
}
