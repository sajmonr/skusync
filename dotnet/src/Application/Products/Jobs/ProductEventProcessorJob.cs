using Application.Events;
using Application.Products.Events;
using Infrastructure.Database;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
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
    ILogger<ProductEventProcessorJob> logger,
    IFeatureManager featureManager,
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext) : IJob
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
    public async Task Execute(IJobExecutionContext context)
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
            return;
        }

        var createdEvents = events.Where(e => e.ChangeType == ProductChangeType.Created).ToArray();
        var updateEvents = events.Where(e => e.ChangeType == ProductChangeType.Updated).ToArray();

        logger.LogInformation(
            "Processing {Total} accumulated product change event(s): {Created} created, {Updated} updated.",
            events.Count, createdEvents.Length, updateEvents.Length);

        await HandleCreatedProducts(createdEvents);
        await HandleUpdatedProducts();

        logger.LogInformation(
            "ProductEventProcessorJob completed. Processed {Total} event(s).",
            events.Count);
    }

    private async Task HandleCreatedProducts(ProductChangedEvent[] events)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack))
        {
            logger.LogInformation(
                "ShopifyWriteBack feature flag is disabled. Skipping Shopify update for created products.");
            return;
        }

        // We need to update SKUs & Barcodes in Shopify.
        // Get all info from DB and group it by product ID.
        // Then update Shopify.
        var variantIds = events.Select(e => e.ProductVariantId).ToArray();
        var items = await dbContext
            .ShopifyProductVariants
            .Where(variant => variantIds.Contains(variant.ShopifyProductVariantId))
            .Select(variant => new { variant.GlobalProductId, variant.GlobalVariantId, variant.Sku, variant.Barcode })
            .ToArrayAsync();
        var groups = items.GroupBy(i => i.GlobalProductId);

        foreach (var group in groups)
        {
            var productId = group.Key;
            var variantsToUpdate =
                group.Select(i => new ShopifyUpdateProductVariant(i.GlobalVariantId, i.Sku, i.Barcode));
            await shopifyProductService.UpdateVariants(productId, variantsToUpdate);
        }
    }

    private async Task HandleUpdatedProducts()
    {
    }
}