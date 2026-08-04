using Application.Products.Services;
using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Application.Jobs;

/// <summary>
/// Registers the Hangfire recurring sync-pipeline jobs from configuration when the processing host
/// (AppServer) starts, and removes any that are disabled or retired. Recurring-job definitions
/// live in Hangfire storage; re-applying them on every boot keeps the schedule in sync with
/// configuration. The Shopify product import reuses <see cref="IProductsService.SyncProducts"/>;
/// the remaining jobs go through <see cref="RecurringJobs"/>.
/// </summary>
public class RecurringJobRegistrar(
    IRecurringJobManager recurringJobManager,
    IOptions<ScheduledJobsOptions> options) : IHostedService
{
    /// <summary>
    /// Job ids from earlier designs whose backing methods no longer exist. Definitions live in
    /// Hangfire storage, so without an explicit removal they would keep firing against deleted
    /// methods after a deploy.
    /// </summary>
    private static readonly string[] RetiredJobIds = ["sku-barcode-sync", "skulabs-title-sync"];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var schedule = options.Value;

        foreach (var retiredJobId in RetiredJobIds)
        {
            recurringJobManager.RemoveIfExists(retiredJobId);
        }

        Apply("shopify-product-sync", schedule.ShopifyProductSync, () =>
            recurringJobManager.AddOrUpdate<IProductsService>(
                "shopify-product-sync",
                service => service.SyncProducts(CancellationToken.None),
                schedule.ShopifyProductSync.Cron));

        Apply("skulabs-item-sync", schedule.SkulabsItemSync, () =>
            recurringJobManager.AddOrUpdate<RecurringJobs>(
                "skulabs-item-sync",
                job => job.SyncSkulabsItems(CancellationToken.None),
                schedule.SkulabsItemSync.Cron));

        Apply("full-reconcile", schedule.FullReconcile, () =>
            recurringJobManager.AddOrUpdate<RecurringJobs>(
                "full-reconcile",
                job => job.ReconcileAll(CancellationToken.None),
                schedule.FullReconcile.Cron));

        Apply("shopify-dispatch", schedule.ShopifyDispatch, () =>
            recurringJobManager.AddOrUpdate<RecurringJobs>(
                "shopify-dispatch",
                job => job.DispatchShopify(CancellationToken.None),
                schedule.ShopifyDispatch.Cron));

        Apply("skulabs-dispatch", schedule.SkulabsDispatch, () =>
            recurringJobManager.AddOrUpdate<RecurringJobs>(
                "skulabs-dispatch",
                job => job.DispatchSkulabs(CancellationToken.None),
                schedule.SkulabsDispatch.Cron));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Apply(string jobId, RecurringJobOptions schedule, Action register)
    {
        if (schedule.Enabled)
        {
            register();
        }
        else
        {
            recurringJobManager.RemoveIfExists(jobId);
        }
    }
}
