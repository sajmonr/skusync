namespace Application.Skulabs.Services;

/// <summary>
/// Keeps every linked SkuLabs item's title in sync with the authoritative display name held
/// on the local Shopify variant row. The variant <c>DisplayName</c> is the master record;
/// when SkuLabs disagrees the value is pushed up to SkuLabs and mirrored into the local
/// <see cref="Infrastructure.Database.Entities.SkulabsItemEntity.Title"/> so the cache
/// matches what is now on the SkuLabs side.
/// </summary>
public interface ISkulabsTitleSyncService
{
    /// <summary>
    /// Finds every linked SkuLabs item whose title differs from its variant's display name
    /// and pushes the variant value up. Intended for the periodic background sweep.
    /// </summary>
    Task<SkulabsTitleSyncResult> SyncAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the same correction flow for the SkuLabs item linked to a single Shopify
    /// variant. Intended for per-event handlers reacting to a variant title change so the
    /// new title is pushed immediately rather than waiting up to one sweep interval.
    /// </summary>
    /// <param name="variantId">The local <see cref="System.Guid"/> primary key of the Shopify variant.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<SkulabsTitleSyncResult> SyncForVariant(Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the same correction flow for a single SkuLabs item identified by its local
    /// primary key. Intended for the per-link event handler so a newly created link is
    /// checked immediately.
    /// </summary>
    /// <param name="skulabsItemId">The local <see cref="System.Guid"/> primary key of the SkuLabs item.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task<SkulabsTitleSyncResult> SyncForSkulabsItem(Guid skulabsItemId, CancellationToken cancellationToken = default);
}
