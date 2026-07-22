namespace Application.Skulabs.Services;

/// <summary>
/// Outcome of a single execution of <see cref="ISkulabsItemSyncService.Sync"/>.
/// Carries the local <see cref="Guid"/> identifiers of every SkuLabs item that was
/// created or updated so the caller can publish downstream events for the delta only.
/// </summary>
public readonly record struct SkulabsItemSyncResult(
    IReadOnlyList<Guid> CreatedSkulabsItemIds,
    IReadOnlyList<Guid> UpdatedSkulabsItemIds,
    int UnmatchedCount,
    int SkippedCount)
{
    public static SkulabsItemSyncResult Empty => new([], [], 0, 0);
}
