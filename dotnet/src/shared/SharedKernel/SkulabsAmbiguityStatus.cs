namespace SharedKernel;

/// <summary>
/// Where a quarantined SkuLabs item sits in the review workflow. Pass 1 only ever writes
/// <see cref="Unresolved"/>; the remaining members exist so a future remap UI can record an
/// operator's decision without a schema change. Persisted via the <c>SkulabsAmbiguityStatuses</c>
/// lookup table — append new members, never renumber.
/// </summary>
public enum SkulabsAmbiguityStatus
{
    /// <summary>Newly surfaced and awaiting review.</summary>
    Unresolved = 1,

    /// <summary>An operator has remapped or otherwise dealt with the item.</summary>
    Resolved = 2,

    /// <summary>An operator has deliberately chosen to leave the item as-is.</summary>
    Ignored = 3
}
