using Application.Events;
using Application.Products.Events;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Products.Jobs;

/// <summary>
/// Represents a job responsible for processing product-related events.
/// </summary>
/// <remarks>
/// This class handles the execution of tasks associated with product events,
/// such as updating product details, syncing inventory states, or responding
/// to other product-driven operations within a system. It is typically
/// triggered in response to specific event occurrences or scheduled tasks.
/// The implementation may include additional integrations with external
/// systems, logging mechanisms, or custom business logic tailored to
/// processing product events efficiently.
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
    public static readonly JobKey Key = new(nameof(ProductEventProcessorJob), "product");

    /// <summary>
    /// Drains all pending product change events and processes the resulting batch.
    /// </summary>
    /// <param name="context">The Quartz execution context providing trigger and timing metadata.</param>
    public Task Execute(IJobExecutionContext context)
    {
        logger.LogDebug(
            "Triggered by '{TriggerKey}'. Scheduled fire time: {ScheduledFireTime}. Actual fire time: {FireTime}.",
            context.Trigger.Key,
            context.ScheduledFireTimeUtc?.ToString("o") ?? "N/A",
            context.FireTimeUtc.ToString("o"));

        var events = eventAccumulator.DrainAll();

        if (events.Count == 0)
        {
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
