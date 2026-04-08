using Application.Shopify;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Jobs;

/// <summary>
/// Quartz.NET job that triggers a full Shopify product synchronisation on each scheduled
/// execution. The <see cref="DisallowConcurrentExecutionAttribute"/> ensures that only one
/// instance runs at a time, preventing overlapping database writes if a sync takes longer
/// than the configured cron interval.
/// </summary>
[DisallowConcurrentExecution]
public class ShopifySyncJob(
    IShopifySyncService shopifySyncService,
    ILogger<ShopifySyncJob> logger) : IJob
{
    /// <summary>
    /// The stable Quartz job key used to identify and reference this job when registering
    /// triggers or querying the scheduler.
    /// </summary>
    public static readonly JobKey Key = new(nameof(ShopifySyncJob), "shopify");

    /// <summary>
    /// Executes the Shopify product synchronisation. Logs timing and trigger details at
    /// <c>Debug</c> level and wraps any exception in a <see cref="JobExecutionException"/>
    /// with <c>refireImmediately: false</c> to prevent an immediate retry loop.
    /// </summary>
    /// <param name="context">The Quartz execution context providing trigger and timing metadata.</param>
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("ShopifySyncJob started.");
        logger.LogDebug(
            "Triggered by '{TriggerKey}'. Scheduled fire time: {ScheduledFireTime}. Actual fire time: {FireTime}.",
            context.Trigger.Key,
            context.ScheduledFireTimeUtc?.ToString("o") ?? "N/A",
            context.FireTimeUtc.ToString("o"));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await shopifySyncService.SynchronizeProducts();
            stopwatch.Stop();

            logger.LogInformation("ShopifySyncJob completed successfully.");
            logger.LogDebug(
                "Job finished in {ElapsedMs}ms. Next scheduled fire time: {NextFireTime}.",
                stopwatch.ElapsedMilliseconds,
                context.NextFireTimeUtc?.ToString("o") ?? "none");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "ShopifySyncJob failed after {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
