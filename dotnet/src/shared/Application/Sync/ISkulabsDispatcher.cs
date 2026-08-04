namespace Application.Sync;

/// <summary>
/// The SkuLabs half of the dispatch stage: drains items marked <c>PendingSkulabsSync</c> and
/// writes their title to SkuLabs in a single <c>bulk_upsert</c>, clearing the flag on success.
/// Gated by the <c>SkulabsWriteBack</c> kill switch — the only place a SkuLabs write is ever
/// gated. Callers decide <em>when</em> to dispatch (scheduled run, manual sync); this component
/// decides <em>how</em>.
/// </summary>
public interface ISkulabsDispatcher
{
    /// <summary>Drains every pending item. The scheduled run and the full sync.</summary>
    Task<DispatchResult> DispatchAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drains the pending items linked to the given variants — items that aren't pending are
    /// simply not part of the run. Used by the manual sync.
    /// </summary>
    Task<DispatchResult> DispatchVariants(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);
}
