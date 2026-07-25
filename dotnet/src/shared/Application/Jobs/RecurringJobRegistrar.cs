using Application.Products.Services;
using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Application.Jobs;

/// <summary>
/// Registers the Hangfire recurring maintenance jobs from configuration when the processing host
/// (AppServer) starts, and removes any that are disabled. Recurring-job definitions live in
/// Hangfire storage; re-applying them on every boot keeps the schedule in sync with configuration.
/// The Shopify product sync reuses <see cref="IProductsService.Sync"/>; the remaining sweeps go
/// through <see cref="RecurringJobs"/>.
/// </summary>
public class RecurringJobRegistrar(
    IRecurringJobManager recurringJobManager,
    IOptions<ScheduledJobsOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var schedule = options.Value;

        Apply("shopify-product-sync", schedule.ShopifyProductSync, () =>
            recurringJobManager.AddOrUpdate<IProductsService>(
                "shopify-product-sync",
                service => service.Sync(CancellationToken.None),
                schedule.ShopifyProductSync.Cron));

        Apply("sku-barcode-sync", schedule.SkuAndBarcodeSync, () =>
            recurringJobManager.AddOrUpdate<RecurringJobs>(
                "sku-barcode-sync",
                job => job.SyncSkuAndBarcodes(CancellationToken.None),
                schedule.SkuAndBarcodeSync.Cron));

        Apply("skulabs-title-sync", schedule.SkulabsTitleSync, () =>
            recurringJobManager.AddOrUpdate<RecurringJobs>(
                "skulabs-title-sync",
                job => job.SyncSkulabsTitles(CancellationToken.None),
                schedule.SkulabsTitleSync.Cron));

        Apply("skulabs-item-sync", schedule.SkulabsItemSync, () =>
            recurringJobManager.AddOrUpdate<RecurringJobs>(
                "skulabs-item-sync",
                job => job.SyncSkulabsItems(CancellationToken.None),
                schedule.SkulabsItemSync.Cron));

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
