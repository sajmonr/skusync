namespace Infrastructure.Database.Entities;

/// <summary>
/// A SkuLabs item that maps to more than one Shopify listing and so was quarantined for review
/// instead of synced. Deliberately separate from <see cref="SkulabsItemEntity"/>: these rows take no
/// part in the active SKU/barcode/title sync. Each carries every Shopify listing SkuLabs reported
/// (see <see cref="Listings"/>) so the ambiguity can be examined and resolved in SkuLabs.
/// </summary>
public class SkulabsAmbiguousItemEntity
{
    public Guid SkulabsAmbiguousItemId { get; set; } = Guid.CreateVersion7();

    /// <summary>The SkuLabs <c>_id</c> of the item. Unique — one quarantine row per source item.</summary>
    public string SkulabsSourceItemId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Upc { get; set; } = string.Empty;

    /// <summary>How many Shopify listings the item had when last seen — the headline signal for reviewers.</summary>
    public int ListingCount { get; set; }

    /// <summary>UTC timestamp this item was first quarantined.</summary>
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent sync run that still found this item ambiguous.</summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public ICollection<SkulabsAmbiguousItemListingEntity> Listings { get; set; } =
        new HashSet<SkulabsAmbiguousItemListingEntity>();
}
