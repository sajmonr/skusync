using Application.Products.Services;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Application.Jobs;

/// <summary>
/// Runs a full, best-effort reconciliation as a single background job — the work behind the
/// dashboard "Sync now" button. Steps run sequentially in dependency order: the Shopify product
/// import must land before the SkuLabs passes, which read the freshly imported variants. Each step
/// self-gates on its own feature flag (inside the service it calls), so a disabled area is simply a
/// no-op. A failing step is logged and the run continues; if any step failed the job still finishes
/// faulted so the operator sees it.
/// </summary>
public class FullSyncOrchestrator(
    IProductsService productsService,
    RecurringJobs recurringJobs,
    ILogger<FullSyncOrchestrator> logger)
{
    // A full sync over a large catalogue can run for many minutes; the distributed lock must outlive
    // the whole run so a second trigger cannot start an overlapping sync while this one is in flight.
    private const int LockTimeoutSeconds = 30 * 60;

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    public async Task RunFullSync(CancellationToken cancellationToken = default)
    {
        var failedSteps = new List<string>();

        await RunStep("Shopify product sync", () => productsService.SyncProducts(cancellationToken), failedSteps);
        await RunStep("SkuLabs item sync", () => recurringJobs.SyncSkulabsItems(cancellationToken), failedSteps);
        await RunStep("SKU/barcode sync", () => recurringJobs.SyncSkuAndBarcodes(cancellationToken), failedSteps);
        await RunStep("SkuLabs title sync", () => recurringJobs.SyncSkulabsTitles(cancellationToken), failedSteps);

        if (failedSteps.Count > 0)
        {
            throw new InvalidOperationException(
                $"Full sync finished with {failedSteps.Count} failed step(s): {string.Join(", ", failedSteps)}.");
        }
    }

    private async Task RunStep(string name, Func<Task> step, List<string> failedSteps)
    {
        logger.LogInformation("Full sync: starting {Step}.", name);
        try
        {
            await step();
            logger.LogInformation("Full sync: {Step} finished.", name);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Full sync: {Step} failed. Continuing with the remaining steps.", name);
            failedSteps.Add(name);
        }
    }
}
