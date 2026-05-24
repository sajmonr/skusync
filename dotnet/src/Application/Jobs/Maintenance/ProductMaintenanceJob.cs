using System.Reflection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Jobs.Maintenance;

/// <summary>
/// Quartz.NET orchestrator that runs every registered <see cref="IMaintenanceTask"/>
/// sequentially. Designed for the nightly maintenance window: tasks share the slot rather
/// than each owning their own cron. A failing task does not abort the run — its exception
/// is logged and the next task continues.
/// </summary>
[DisallowConcurrentExecution]
public class ProductMaintenanceJob(
    IEnumerable<IMaintenanceTask> tasks,
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
        var taskList = OrderTasks(tasks);
        var succeeded = 0;
        var failed = 0;

        foreach (var task in taskList)
        {
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
            taskList.Count,
            succeeded,
            failed
        );

        static List<IMaintenanceTask> OrderTasks(IEnumerable<IMaintenanceTask> source) =>
            source
                .Select((task, registrationIndex) => (
                    Task: task,
                    Order: task.GetType().GetCustomAttribute<TaskOrderAttribute>()?.Order,
                    RegistrationIndex: registrationIndex))
                .OrderBy(x => x.Order is null)
                .ThenBy(x => x.Order ?? int.MaxValue)
                .ThenBy(x => x.RegistrationIndex)
                .Select(x => x.Task)
                .ToList();

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
