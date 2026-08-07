namespace Application.Skulabs.Services;

/// <summary>
/// Outcome of a single execution of <see cref="ISkulabsItemSyncService.Sync"/>.
/// Carries the local <see cref="Guid"/> identifiers of every SkuLabs item that was
/// created or re-linked so the caller can publish downstream events for the delta only.
/// </summary>
/// <param name="CreatedSkulabsItemIds">Items seen for the first time.</param>
/// <param name="UpdatedSkulabsItemIds">Items whose resolved Shopify variant changed.</param>
/// <param name="UnresolvedListingCount">
/// Listings naming a Shopify variant we do not hold. Informational — the listing is still stored so
/// the gap is visible.
/// </param>
/// <param name="SkippedCount">Items that threw while being reconciled and were left untouched.</param>
/// <param name="RemovedCount">Items SkuLabs no longer reports, deleted along with their listings.</param>
/// <param name="AmbiguousCount">
/// Items carrying more than one Shopify listing after this run. A gauge of the current state rather
/// than a delta — ambiguity is derived from listing cardinality, so there is nothing to create or
/// remove.
/// </param>
public readonly record struct SkulabsItemSyncResult(
    IReadOnlyList<Guid> CreatedSkulabsItemIds,
    IReadOnlyList<Guid> UpdatedSkulabsItemIds,
    int UnresolvedListingCount,
    int SkippedCount,
    int RemovedCount,
    int AmbiguousCount)
{
    public static SkulabsItemSyncResult Empty => new([], [], 0, 0, 0, 0);
}
