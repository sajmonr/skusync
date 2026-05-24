using Application.Jobs.Maintenance;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;

namespace Application.Skulabs.Maintenance;

/// <summary>
/// Maintenance task that reconciles every Shopify variant's SKU and barcode against the
/// authoritative SkuLabs values. The outbound Shopify write inside the service is gated by
/// the <c>ShopifyWriteBack</c> feature flag; this task runs unconditionally so the local
/// view of "what should be in Shopify" stays up to date even when external writes are off.
/// Invoked by <see cref="ProductMaintenanceJob"/>; not registered as a standalone Quartz job.
/// </summary>
public class SkuAndBarcodeSyncTask(
    ISkuAndBarcodeSyncService syncService,
    ILogger<SkuAndBarcodeSyncTask> logger
) : IMaintenanceTask
{
    public string Name => nameof(SkuAndBarcodeSyncTask);

    public async Task Execute(CancellationToken cancellationToken)
    {
        var result = await syncService.SyncAll(cancellationToken);

        logger.LogInformation(
            "SkuAndBarcodeSyncTask reconciliation: Checked: {Checked}, Drifted: {Drifted}, Corrected: {Corrected}, Failed: {Failed}.",
            result.Checked,
            result.Drifted,
            result.Corrected,
            result.Failed
        );
    }
}
