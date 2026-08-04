namespace Infrastructure.Database.Entities;

public class SkulabsItemEntity
{

    public Guid SkulabsItemId { get; set; }  = Guid.CreateVersion7();

    public Guid ShopifyProductVariantId { get; set; } = Guid.Empty;
    
    public string SkulabsSourceItemId { get; set; } = string.Empty;

    public string SkulabsSourceListingId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

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

    public ShopifyProductVariantEntity? ShopifyProductVariant { get; set; }

}