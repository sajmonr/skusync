using Application.Products.Services;
using Application.Sync;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Application.Jobs;

/// <summary>
/// Runs a full, best-effort pass over the whole pipeline as a single background job — the work
/// behind the dashboard "Sync now" button. Steps run sequentially in dependency order: the imports
/// must land before the reconcile, which feeds the dispatchers. A failing step is logged and the
/// run continues; if any step failed the job still finishes faulted so the operator sees it.
/// </summary>
/// <remarks>
/// "Sync now" is a manual trigger, so the dispatch steps call the dispatchers directly — bypassing
/// the <c>ShopifyAutoDispatch</c>/<c>SkulabsAutoDispatch</c> gates the scheduled runs honour. The
/// dispatchers' own <c>*WriteBack</c> kill switches still apply.
/// </remarks>
public class FullSyncOrchestrator(
    IProductsService productsService,
    RecurringJobs recurringJobs,
    IReconciler reconciler,
    IShopifyDispatcher shopifyDispatcher,
    ISkulabsDispatcher skulabsDispatcher,
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
        await RunStep("Reconcile", () => reconciler.ReconcileAll(cancellationToken), failedSteps);
        await RunStep("Shopify dispatch", () => shopifyDispatcher.DispatchAll(cancellationToken), failedSteps);
        await RunStep("SkuLabs dispatch", () => skulabsDispatcher.DispatchAll(cancellationToken), failedSteps);

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
