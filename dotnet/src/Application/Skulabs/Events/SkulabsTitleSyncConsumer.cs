using Application.Products.Events;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace Application.Skulabs.Events;

/// <summary>
/// Reacts to every event that could change a variant's authoritative display name or could
/// link a SkuLabs item to a variant, and pushes the variant's <c>DisplayName</c> up to the
/// matching SkuLabs item if it differs. The feature-flag check and HTTP call live inside
/// <see cref="ISkulabsTitleSyncService"/>; this consumer is a thin event-to-service shim.
/// </summary>
public class SkulabsTitleSyncConsumer(
    ISkulabsTitleSyncService syncService,
    ILogger<SkulabsTitleSyncConsumer> logger
)
    : IConsumer<ProductVariantCreatedEvent>,
        IConsumer<ProductVariantUpdatedEvent>,
        IConsumer<SkulabsProductImportedEvent>
{
    public async Task OnHandle(ProductVariantCreatedEvent message, CancellationToken cancellationToken)
    {
        try
        {
            await syncService.SyncForVariant(message.ProductVariantId, cancellationToken);
        }
        catch (Exception exception)
        {
            // Swallow per-variant failures so a single bad item doesn't poison the consumer.
            // The periodic SkulabsTitleSyncTask will retry.
            logger.LogError(
                exception,
                "SkuLabs title sync failed for created variant {VariantId}. The periodic title sync will retry.",
                message.ProductVariantId);
        }
    }

    public async Task OnHandle(ProductVariantUpdatedEvent message, CancellationToken cancellationToken)
    {
        try
        {
            await syncService.SyncForVariant(message.ProductVariantId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "SkuLabs title sync failed for updated variant {VariantId}. The periodic title sync will retry.",
                message.ProductVariantId);
        }
    }

    public async Task OnHandle(SkulabsProductImportedEvent message, CancellationToken cancellationToken)
    {
        try
        {
            await syncService.SyncForSkulabsItem(message.SkulabsProductId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "SkuLabs title sync failed for linked SkuLabs item {SkulabsItemId}. The periodic title sync will retry.",
                message.SkulabsProductId);
        }
    }
}
