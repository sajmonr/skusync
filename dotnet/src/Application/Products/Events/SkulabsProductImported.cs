using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace Application.Products.Events;

public readonly record struct SkulabsProductImportedEvent(Guid SkulabsProductId);

/// <summary>
/// Consumes <see cref="SkulabsProductImportedEvent"/> messages published when a SkuLabs item
/// has just been linked (or re-linked) to a Shopify variant in the local database, and
/// immediately checks whether the variant's Shopify SKU/barcode matches the authoritative
/// SkuLabs values — correcting Shopify if they have drifted. This runs unconditionally; the
/// outbound Shopify write itself is gated by the <c>ShopifyWriteBack</c> feature flag inside
/// <see cref="ISkuAndBarcodeSyncService"/>.
/// </summary>
public class SkulabsProductImportedConsumer(
    ISkuAndBarcodeSyncService syncService,
    ILogger<SkulabsProductImportedConsumer> logger
) : IConsumer<SkulabsProductImportedEvent>
{
    public async Task OnHandle(SkulabsProductImportedEvent message, CancellationToken cancellationToken)
    {
        try
        {
            await syncService.SyncForSkulabsItem(message.SkulabsProductId, cancellationToken);
        }
        catch (Exception exception)
        {
            // Swallow per-item failures so a single bad item doesn't poison the batch published
            // from SkulabsItemSyncJob. The periodic SkuAndBarcodeSyncJob will retry.
            logger.LogError(
                exception,
                "Post-link drift check failed for SkuLabs item {SkulabsItemId}. The periodic drift sync will retry.",
                message.SkulabsProductId);
        }
    }
}
