namespace Application.Skulabs.Services;

/// <summary>
/// Detects drift between the SKU / barcode held on a Shopify product variant in the local
/// database and the authoritative values held on the linked SkuLabs item, and corrects
/// Shopify (and the local mirror) so the two agree. SkuLabs is treated as the master record
/// because once a barcode is recorded in SkuLabs it cannot change.
/// </summary>
public interface IShopifyVariantDriftSyncService
{
    /// <summary>
    /// Finds every Shopify variant linked to a SkuLabs item whose SKU or barcode differs from
    /// the SkuLabs values, pushes corrections to Shopify and mirrors them locally. Intended for
    /// the periodic background sweep.
    /// </summary>
    Task<ShopifyVariantDriftSyncResult> SyncAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the same correction flow for a single SkuLabs item identified by its local
    /// primary key. Intended for the per-link event handler so a newly created link is checked
    /// immediately rather than waiting up to one sweep interval.
    /// </summary>
    /// <param name="skulabsItemId">The local <see cref="System.Guid"/> primary key of the SkuLabs item.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<ShopifyVariantDriftSyncResult> SyncForSkulabsItem(Guid skulabsItemId, CancellationToken cancellationToken = default);
}
