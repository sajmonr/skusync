namespace Application.Sync;

/// <summary>
/// The Shopify half of the dispatch stage: drains variants marked
/// <c>PendingShopifySync</c> and writes their SKU/barcode to Shopify, clearing the flag on
/// success. Gated by the <c>ShopifyWriteBack</c> kill switch — the only place a Shopify write is
/// ever gated. Callers decide <em>when</em> to dispatch (scheduled run, immediate post-commit
/// dispatch after SKU generation, manual sync); this component decides <em>how</em>.
/// </summary>
public interface IShopifyDispatcher
{
    /// <summary>Drains every pending variant. The scheduled run and the full sync.</summary>
    Task<DispatchResult> DispatchAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drains the pending variants among the given ids — variants that aren't pending are simply
    /// not part of the run. Used by the immediate post-commit dispatch and the manual sync.
    /// </summary>
    Task<DispatchResult> DispatchVariants(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);
}
