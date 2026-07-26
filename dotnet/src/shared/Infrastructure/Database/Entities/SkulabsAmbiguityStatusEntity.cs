using SharedKernel;

namespace Infrastructure.Database.Entities;

/// <summary>
/// Lookup row naming a <see cref="SkulabsAmbiguityStatus"/> so the workflow state on an ambiguous
/// item reads as a word in the database rather than a bare integer. The key is the enum itself
/// (stored as its int value); seeded from the enum members.
/// </summary>
public class SkulabsAmbiguityStatusEntity
{
    public SkulabsAmbiguityStatus Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<SkulabsAmbiguousItemEntity> AmbiguousItems { get; set; } =
        new HashSet<SkulabsAmbiguousItemEntity>();
}
