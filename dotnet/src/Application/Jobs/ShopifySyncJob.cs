using Application.Shopify;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Jobs;

[DisallowConcurrentExecution]
public class ShopifySyncJob(
    IShopifySyncService shopifySyncService,
    ILogger<ShopifySyncJob> logger) : IJob
{
    public static readonly JobKey Key = new(nameof(ShopifySyncJob), "shopify");

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
