namespace Application.Skulabs.Services;

/// <summary>
/// Outcome of a single <see cref="ISkulabsTitleSyncService"/> run.
/// </summary>
/// <param name="Checked">Number of linked SkuLabs items examined for title drift.</param>
/// <param name="Drifted">Subset of <paramref name="Checked"/> whose SkuLabs item title differed from the variant's display name.</param>
/// <param name="Corrected">Number of items whose SkuLabs title was successfully overwritten with the variant's display name.</param>
/// <param name="Failed">Number of items where the SkuLabs update threw or the writeback flag was disabled; their local row was left untouched and will be retried on the next run.</param>
public readonly record struct SkulabsTitleSyncResult(
    int Checked,
    int Drifted,
    int Corrected,
    int Failed)
{
    public static SkulabsTitleSyncResult Empty => new(0, 0, 0, 0);
}
