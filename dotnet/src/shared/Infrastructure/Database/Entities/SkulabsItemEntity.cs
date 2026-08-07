namespace Infrastructure.Database.Entities;

/// <summary>
/// A SkuLabs inventory item, mirrored locally. One row per SkuLabs <c>_id</c> regardless of how many
/// Shopify listings it has — whether an item takes part in the active sync is <em>derived</em> from
/// <see cref="Listings"/> (see <see cref="SkulabsItemLinks.IsSyncable"/>), never from which table it
/// sits in. An item with several listings is ambiguous and simply fails that predicate.
/// </summary>
public class SkulabsItemEntity
{
    public Guid SkulabsItemId { get; set; } = Guid.CreateVersion7();

    /// <summary>The SkuLabs <c>_id</c> of the item. Unique — one row per source item.</summary>
    public string SkulabsSourceItemId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    /// <summary>The item's UPC/EAN as SkuLabs reports it under <c>upc</c>.</summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// The item's bin location in the configured SkuLabs warehouse (e.g. <c>A-01-06</c>), or empty
    /// when it has none. An inbound-only mirror: SkuLabs owns it, we never push it back, so it is
    /// refreshed on every sync run and a change to it must never set
    /// <see cref="PendingSkulabsSync"/>.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this item's local mirror diverges from SkuLabs —
    /// the title has been corrected locally but not yet pushed. Set where the divergence is
    /// originated (the reconciler); cleared by the SkuLabs dispatcher on a confirmed push.
    /// Doubles as the "pending sync" status shown in the Item Sync grid.
    /// </summary>
    public bool PendingSkulabsSync { get; set; }

    /// <summary>
    /// Gets or sets the number of consecutive failed attempts to push this item to SkuLabs.
    /// Reset to zero on a successful push. Rate-limited runs do not count — rate limiting means
    /// "later", not "broken". At the exclusion threshold the item is skipped by future dispatch
    /// runs so a permanently rejected item cannot poison every batch.
    /// </summary>
    public int FailedSkulabsSyncAttempts { get; set; }

    /// <summary>UTC timestamp this item was first seen in a SkuLabs payload.</summary>
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the most recent sync run that still found this item. Together with
    /// <see cref="FirstSeenUtc"/> this is how long an ambiguous item has been ambiguous, which
    /// cannot be recovered from listing cardinality alone.
    /// </summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Every Shopify listing SkuLabs reports for this item. Zero means SkuLabs-only, one means a
    /// candidate for the active sync, more than one means ambiguous.
    /// </summary>
    public ICollection<SkulabsItemListingEntity> Listings { get; set; } =
        new HashSet<SkulabsItemListingEntity>();
}
