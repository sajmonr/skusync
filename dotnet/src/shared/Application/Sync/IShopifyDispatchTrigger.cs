namespace Application.Sync;

/// <summary>
/// The immediate, automatic Shopify dispatch trigger used by ingest writers right after they
/// commit an originated divergence (a generated SKU, a Shopify-side drift, a deduplication
/// rewrite) so the correction reaches Shopify within seconds instead of waiting for the scheduled
/// run. Best-effort by design: it honours the <c>ShopifyAutoDispatch</c> flag and swallows
/// dispatch failures — the rows are already pending, so the scheduled dispatch retries them.
/// </summary>
public interface IShopifyDispatchTrigger
{
    /// <summary>
    /// Dispatches the pending variants among <paramref name="variantIds"/> when automatic dispatch
    /// is enabled. Never throws — a failure is logged and left for the scheduled run.
    /// </summary>
    Task TryDispatch(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);
}
