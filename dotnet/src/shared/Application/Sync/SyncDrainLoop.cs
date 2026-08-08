using Application.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Sync;

/// <summary>
/// Drains pending rows to Shopify and SkuLabs on a short tick, so a change reaches its target in
/// seconds rather than on the scheduled jobs' cadence.
/// <para>
/// A background loop rather than another recurring job because Hangfire's cron cannot go below a
/// minute, and the workarounds are worse than the loop: a self-rescheduling chain stops silently if
/// a link is ever lost, and a job firing every ten seconds would write thousands of job and state
/// rows a day, almost all of them recording that there was nothing to do.
/// </para>
/// <para>
/// Nothing here owns correctness. The pending flags are durable, the dispatchers are idempotent,
/// and the scheduled jobs still sweep — deleting this class would slow the system down without
/// breaking it. That is what makes a plain timer sufficient: it needs no durability, no retry, and
/// no per-run status, which is precisely what a job scheduler would have supplied.
/// </para>
/// </summary>
public class SyncDrainLoop(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncDrainLoopOptions> options,
    TimeProvider timeProvider,
    ILogger<SyncDrainLoop> logger) : BackgroundService
{
    private DateTimeOffset _lastSkulabsPush = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Sync drain loop is disabled. Pending rows will be drained by the scheduled dispatch jobs only.");
            return;
        }

        var tick = TimeSpan.FromSeconds(settings.TickSeconds);
        var skulabsInterval = TimeSpan.FromSeconds(settings.SkulabsMinimumPushIntervalSeconds);

        logger.LogInformation(
            "Sync drain loop started. Tick {TickSeconds}s, minimum SkuLabs push interval {SkulabsIntervalSeconds}s.",
            settings.TickSeconds, settings.SkulabsMinimumPushIntervalSeconds);

        using var timer = new PeriodicTimer(tick, timeProvider);

        while (await SafeWaitForNextTick(timer, stoppingToken))
        {
            try
            {
                await Drain(skulabsInterval, stoppingToken);
            }
            catch (Exception exception)
            {
                // One bad tick must not end the loop: the next one retries, and the rows it failed
                // to drain are still pending. Swallowing here is the difference between a blip and
                // silently falling back to the scheduled cadence for the rest of the process's life.
                logger.LogError(exception, "Sync drain tick failed. The next tick will retry.");
            }
        }
    }

    private async Task Drain(TimeSpan skulabsInterval, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();

        // Routed through the same jobs the scheduler calls, so the feature-flag gating and the
        // logging are defined once and cannot drift between the two ways of reaching a dispatcher.
        await jobs.DispatchShopify(cancellationToken);

        if (timeProvider.GetUtcNow() - _lastSkulabsPush < skulabsInterval)
        {
            return;
        }

        var result = await jobs.DrainSkulabs(cancellationToken);

        // Timed from finding work, not from a successful push. A rate-limited or rejected run has
        // still spent a request against the quota, and the interval exists to bound requests rather
        // than successes — restarting the clock only on success would retry a failing target every
        // tick, which is exactly the situation where spending least matters most.
        if (result.Pending > 0)
        {
            _lastSkulabsPush = timeProvider.GetUtcNow();
        }
    }

    /// <summary>
    /// Waits for the next tick, treating shutdown as a clean stop rather than an error.
    /// </summary>
    private static async Task<bool> SafeWaitForNextTick(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
