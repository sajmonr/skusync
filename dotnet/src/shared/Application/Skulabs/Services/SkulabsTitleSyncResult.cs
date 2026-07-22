namespace Application.Skulabs.Services;

/// <summary>
/// Outcome of a single <see cref="ISkulabsTitleSyncService"/> run.
/// </summary>
/// <param name="Checked">Number of linked SkuLabs items examined — drifted titles plus rows already marked pending.</param>
/// <param name="Drifted">Subset of <paramref name="Checked"/> whose SkuLabs item title differed from the variant's display name. Pending-only rows are not counted here.</param>
/// <param name="Corrected">Number of items that reached their desired state in this run. When the writeback flag is enabled and the push succeeds this also means SkuLabs was updated; when disabled it counts drifted rows that were mirrored locally and flagged with <see cref="Infrastructure.Database.Entities.SkulabsItemEntity.PendingSkulabsSync"/>.</param>
/// <param name="Failed">Number of items where the SkuLabs update threw; their local row and pending flag were left untouched and will be retried on the next run.</param>
public readonly record struct SkulabsTitleSyncResult(
    int Checked,
    int Drifted,
    int Corrected,
    int Failed)
{
    public static SkulabsTitleSyncResult Empty => new(0, 0, 0, 0);
}
