using SharedKernel;

namespace Infrastructure.Database.Entities;

/// <summary>
/// A SkuLabs item that could not be cleanly mapped to exactly one Shopify variant and so was
/// quarantined for review instead of synced. Deliberately separate from <see cref="SkulabsItemEntity"/>:
/// these rows take no part in the active SKU/barcode/title sync. Each carries every listing SkuLabs
/// reported (see <see cref="Listings"/>) so the ambiguity can be examined and remapped later.
/// </summary>
public class SkulabsAmbiguousItemEntity
{
    public Guid SkulabsAmbiguousItemId { get; set; } = Guid.CreateVersion7();

    /// <summary>The SkuLabs <c>_id</c> of the item. Unique — one quarantine row per source item.</summary>
    public string SkulabsSourceItemId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Upc { get; set; } = string.Empty;

    /// <summary>How many listings the item had when last seen — the headline signal for reviewers.</summary>
    public int ListingCount { get; set; }

    public SkulabsAmbiguityReason Reason { get; set; }

    /// <summary>Review-workflow state. Always <see cref="SkulabsAmbiguityStatus.Unresolved"/> in pass 1.</summary>
    public SkulabsAmbiguityStatus Status { get; set; } = SkulabsAmbiguityStatus.Unresolved;

    /// <summary>UTC timestamp this item was first quarantined.</summary>
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent sync run that still found this item ambiguous.</summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public SkulabsAmbiguityReasonEntity? ReasonNavigation { get; set; }

    public SkulabsAmbiguityStatusEntity? StatusNavigation { get; set; }

    public ICollection<SkulabsAmbiguousItemListingEntity> Listings { get; set; } =
        new HashSet<SkulabsAmbiguousItemListingEntity>();
}
