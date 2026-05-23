using Application.Jobs;
using Application.Products.Events;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Quartz;
using SlimMessageBus;

namespace Application.Skulabs.Jobs;

/// <summary>
/// Quartz.NET job that periodically pulls every SkuLabs item, links it to the
/// matching Shopify product variant in the local database, and publishes a
/// <see cref="SkulabsProductImportedEvent"/> for each created or updated record.
/// </summary>
[DisallowConcurrentExecution]
public class SkulabsItemSyncJob(
    ISkulabsItemSyncService syncService,
    IMessageBus messageBus,
    IFeatureManager featureManager,
    ILogger<SkulabsItemSyncJob> logger
) : IJob
{
    public static readonly JobKey Key = new(nameof(SkulabsItemSyncJob), "skulabs");

    public async Task Execute(IJobExecutionContext context)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.SkulabsSyncEnabled))
        {
            logger.LogDebug(
                "{Flag} is disabled. SkulabsItemSyncJob fired but is doing nothing.",
                FeatureFlags.SkulabsSyncEnabled);
            return;
        }

        logger.LogInformation("SkulabsItemSyncJob started.");
        logger.LogDebug(
            "Triggered by '{TriggerKey}'. Scheduled fire time: {ScheduledFireTime}. Actual fire time: {FireTime}.",
            context.Trigger.Key,
            context.ScheduledFireTimeUtc?.ToString("o") ?? "N/A",
            context.FireTimeUtc.ToString("o")
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await syncService.Sync(context.CancellationToken);

            var affected = result.CreatedSkulabsItemIds.Concat(result.UpdatedSkulabsItemIds);
            await messageBus.PublishBatch(
                affected.Select(id => new SkulabsProductImportedEvent(id))
            );

            stopwatch.Stop();
            logger.LogInformation(
                "SkulabsItemSyncJob completed successfully in {ElapsedMs}ms. Created: {Created}, Updated: {Updated}.",
                stopwatch.ElapsedMilliseconds,
                result.CreatedSkulabsItemIds.Count,
                result.UpdatedSkulabsItemIds.Count
            );
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(
                exception,
                "SkulabsItemSyncJob failed after {ElapsedMs}ms.",
                stopwatch.ElapsedMilliseconds
            );
            throw new JobExecutionException(exception, refireImmediately: false);
        }
    }
}
