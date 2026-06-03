using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Jobs.Maintenance;

/// <summary>
/// Quartz.NET orchestrator that runs every registered <see cref="IMaintenanceTask"/>
/// sequentially. Designed for the nightly maintenance window: tasks share the slot rather
/// than each owning their own cron. A failing task does not abort the run — its exception
/// is logged and the next task continues.
/// </summary>
/// <remarks>
/// Each task is resolved from its own DI scope so that it gets a fresh
/// <c>ApplicationDbContext</c>. This isolation is deliberate: a task whose
/// <c>SaveChanges</c> throws leaves its <c>Added</c>/<c>Modified</c> entities tracked on
/// that context, and a shared context would let the next task's <c>SaveChanges</c> re-flush
/// those dangling changes — producing constraint violations attributed to the wrong task.
/// </remarks>
[DisallowConcurrentExecution]
public class ProductMaintenanceJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ProductMaintenanceJob> logger
) : IJob
{
    public static readonly JobKey Key = new(nameof(ProductMaintenanceJob), "maintenance");

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("ProductMaintenanceJob started.");
        logger.LogDebug(
            "Triggered by '{TriggerKey}'. Scheduled fire time: {ScheduledFireTime}. Actual fire time: {FireTime}.",
            context.Trigger.Key,
            context.ScheduledFireTimeUtc?.ToString("o") ?? "N/A",
            context.FireTimeUtc.ToString("o")
        );

        var jobStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var orderedTaskIndices = ResolveOrderedTaskIndices();
        var succeeded = 0;
        var failed = 0;

        foreach (var taskIndex in orderedTaskIndices)
        {
            // Fresh scope per task → fresh DbContext, so a failed SaveChanges in one task
            // cannot leak tracked entities into the next task's SaveChanges. See the
            // remarks on this class. Tasks are correlated across scopes by their registration
            // index, which Microsoft DI keeps stable for IEnumerable<T> resolution.
            using var scope = scopeFactory.CreateScope();
            var task = scope.ServiceProvider
                .GetServices<IMaintenanceTask>()
                .ElementAt(taskIndex);

            await RunTask(task, context.CancellationToken);
            if (context.CancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "ProductMaintenanceJob cancellation requested — skipping remaining maintenance tasks."
                );
                break;
            }
        }

        jobStopwatch.Stop();
        logger.LogInformation(
            "ProductMaintenanceJob completed in {ElapsedMs}ms. Tasks run: {TaskCount}, Succeeded: {Succeeded}, Failed: {Failed}.",
            jobStopwatch.ElapsedMilliseconds,
            orderedTaskIndices.Count,
            succeeded,
            failed
        );

        List<int> ResolveOrderedTaskIndices()
        {
            using var scope = scopeFactory.CreateScope();
            return scope.ServiceProvider
                .GetServices<IMaintenanceTask>()
                .Select((task, registrationIndex) => (
                    RegistrationIndex: registrationIndex,
                    Order: task.GetType().GetCustomAttribute<TaskOrderAttribute>()?.Order))
                .OrderBy(x => x.Order is null)
                .ThenBy(x => x.Order ?? int.MaxValue)
                .ThenBy(x => x.RegistrationIndex)
                .Select(x => x.RegistrationIndex)
                .ToList();
        }

        async Task RunTask(IMaintenanceTask task, CancellationToken cancellationToken)
        {
            logger.LogInformation("Maintenance task '{TaskName}' started.", task.Name);
            var taskStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await task.Execute(cancellationToken);
                taskStopwatch.Stop();
                succeeded++;
                logger.LogInformation(
                    "Maintenance task '{TaskName}' completed in {ElapsedMs}ms.",
                    task.Name,
                    taskStopwatch.ElapsedMilliseconds
                );
            }
            catch (Exception exception)
            {
                taskStopwatch.Stop();
                failed++;
                logger.LogError(
                    exception,
                    "Maintenance task '{TaskName}' failed after {ElapsedMs}ms. Continuing with remaining tasks.",
                    task.Name,
                    taskStopwatch.ElapsedMilliseconds
                );
            }
        }
    }
}
