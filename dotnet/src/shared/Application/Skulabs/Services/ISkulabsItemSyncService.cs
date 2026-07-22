namespace Application.Skulabs.Services;

/// <summary>
/// Synchronises SkuLabs inventory items into the local database, linking them to
/// existing Shopify product variants by Shopify variant ID.
/// </summary>
public interface ISkulabsItemSyncService
{
    /// <summary>
    /// Fetches every SkuLabs item, matches each one to a Shopify product variant already in
    /// the database (by numeric Shopify variant ID) and upserts the linked SkuLabs item record.
    /// Items whose variant is unknown locally are skipped. The returned result lists the local
    /// identifiers of every record that was created or updated, so callers can publish
    /// downstream events only for actual changes.
    /// </summary>
    Task<SkulabsItemSyncResult> Sync(CancellationToken cancellationToken = default);
}
