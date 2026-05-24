using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Skulabs.Jobs;

/// <summary>
/// Quartz.NET job that periodically reconciles Shopify variant SKU and barcode values against
/// their linked SkuLabs items. SkuLabs is authoritative — any variant whose values have
/// drifted is corrected back to the SkuLabs side. The outbound Shopify write itself is gated
/// inside <see cref="ISkuAndBarcodeSyncService"/> by the <c>ShopifyWriteBack</c> feature
/// flag; this job runs unconditionally so the local view of "what should be in Shopify" stays
/// up to date even when external writes are disabled.
/// </summary>
[DisallowConcurrentExecution]
public class SkuAndBarcodeSyncJob(
    ISkuAndBarcodeSyncService syncService,
    ILogger<SkuAndBarcodeSyncJob> logger
) : IJob
{
    public static readonly JobKey Key = new(nameof(SkuAndBarcodeSyncJob), "skulabs");

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("SkuAndBarcodeSyncJob started.");
        logger.LogDebug(
            "Triggered by '{TriggerKey}'. Scheduled fire time: {ScheduledFireTime}. Actual fire time: {FireTime}.",
            context.Trigger.Key,
            context.ScheduledFireTimeUtc?.ToString("o") ?? "N/A",
            context.FireTimeUtc.ToString("o")
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await syncService.SyncAll(context.CancellationToken);

            stopwatch.Stop();
            logger.LogInformation(
                "SkuAndBarcodeSyncJob completed in {ElapsedMs}ms. Checked: {Checked}, Drifted: {Drifted}, Corrected: {Corrected}, Failed: {Failed}.",
                stopwatch.ElapsedMilliseconds,
                result.Checked,
                result.Drifted,
                result.Corrected,
                result.Failed
            );
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(
                exception,
                "SkuAndBarcodeSyncJob failed after {ElapsedMs}ms.",
                stopwatch.ElapsedMilliseconds
            );
            throw new JobExecutionException(exception, refireImmediately: false);
        }
    }
}
