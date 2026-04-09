using Application.Events;
using Application.Products.Events;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Jobs;

/// <summary>
/// Quartz.NET job that runs every N minutes (configurable via <c>ScheduledJobs:ProductEventProcessor</c>),
/// drains all accumulated <see cref="ProductChangedEvent"/> instances from the
/// <see cref="IEventAccumulator"/> singleton, and processes them as a batch.
/// </summary>
/// <remarks>
/// The <see cref="DisallowConcurrentExecutionAttribute"/> ensures at most one instance runs at a
/// time. Because the accumulator is an in-memory queue, events produced between the previous job
/// run and the current one are safely captured regardless of how long the previous run took.
/// </remarks>
[DisallowConcurrentExecution]
public class ProductEventProcessorJob(
    IEventAccumulator<ProductChangedEvent> eventAccumulator,
    ILogger<ProductEventProcessorJob> logger) : IJob
{
    /// <summary>
    /// The stable Quartz job key used to identify and reference this job when registering
    /// triggers or querying the scheduler.
    /// </summary>
    public static readonly JobKey Key = new(nameof(ProductEventProcessorJob), "product-events");

    /// <summary>
    /// Drains all pending product change events and processes the resulting batch.
    /// </summary>
    /// <param name="context">The Quartz execution context providing trigger and timing metadata.</param>
    public Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("ProductEventProcessorJob started.");
        logger.LogDebug(
            "Triggered by '{TriggerKey}'. Scheduled fire time: {ScheduledFireTime}. Actual fire time: {FireTime}.",
            context.Trigger.Key,
            context.ScheduledFireTimeUtc?.ToString("o") ?? "N/A",
            context.FireTimeUtc.ToString("o"));

        var events = eventAccumulator.DrainAll();

        if (events.Count == 0)
        {
            logger.LogDebug("No product change events accumulated since the last run.");
            logger.LogInformation("ProductEventProcessorJob completed. No events to process.");
            return Task.CompletedTask;
        }

        var createdCount = events.Count(e => e.ChangeType == ProductChangeType.Created);
        var updatedCount = events.Count(e => e.ChangeType == ProductChangeType.Updated);

        logger.LogInformation(
            "Processing {Total} accumulated product change event(s): {Created} created, {Updated} updated.",
            events.Count, createdCount, updatedCount);

        // TODO: add downstream business logic here, e.g.:
        //   - push changed variant IDs to an external system (SkuLabs, etc.)
        //   - trigger a reconciliation task
        //   - publish a summary notification

        logger.LogInformation(
            "ProductEventProcessorJob completed. Processed {Total} event(s).",
            events.Count);

        return Task.CompletedTask;
    }
}
