using Application.Products.Events;
using Application.Skulabs.Services;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using SlimMessageBus;

namespace Application.Jobs;

/// <summary>
/// Entry points for the scheduled maintenance sweeps, invoked by Hangfire recurring jobs. This is
/// the whole of the scheduled-work surface — the previous per-task classes and the maintenance-task
/// abstraction are gone. Each method is an independent recurring job (staggered by cron), so one
/// failing sweep never blocks the others. The Shopify product sync is scheduled directly against
/// <see cref="IProductsService.Sync"/> and so has no method here.
/// </summary>
public class RecurringJobs(
    ISkuAndBarcodeSyncService skuAndBarcodeSyncService,
    ISkulabsTitleSyncService skulabsTitleSyncService,
    ISkulabsItemSyncService skulabsItemSyncService,
    IMessageBus messageBus,
    IFeatureManager featureManager,
    ILogger<RecurringJobs> logger)
{
    /// <summary>Reconciles every variant's SKU and barcode against the authoritative SkuLabs values.</summary>
    public async Task SyncSkuAndBarcodes(CancellationToken cancellationToken = default)
    {
        var result = await skuAndBarcodeSyncService.SyncAll(cancellationToken);

        logger.LogInformation(
            "SKU/barcode reconciliation: Checked: {Checked}, Drifted: {Drifted}, Corrected: {Corrected}, Failed: {Failed}.",
            result.Checked, result.Drifted, result.Corrected, result.Failed);
    }

    /// <summary>Reconciles every linked SkuLabs item's title with the authoritative variant display name.</summary>
    public async Task SyncSkulabsTitles(CancellationToken cancellationToken = default)
    {
        var result = await skulabsTitleSyncService.SyncAll(cancellationToken);

        logger.LogInformation(
            "SkuLabs title reconciliation: Checked: {Checked}, Drifted: {Drifted}, Corrected: {Corrected}, Failed: {Failed}.",
            result.Checked, result.Drifted, result.Corrected, result.Failed);
    }

    /// <summary>
    /// Pulls every SkuLabs item, links it to the matching Shopify variant, surfaces the ones that
    /// cannot be cleanly mapped, and publishes a <see cref="SkulabsProductImportedEvent"/> for each
    /// created or re-linked record. Gated by the <see cref="FeatureFlags.SkulabsSyncEnabled"/> flag —
    /// when disabled the sweep is a no-op. The reconciler loads all state into memory and saves once,
    /// so two overlapping runs would race; the lock keeps this recurring job single-flight (the
    /// manual full sync guards itself the same way).
    /// </summary>
    [DisableConcurrentExecution(30 * 60)]
    public async Task SyncSkulabsItems(CancellationToken cancellationToken = default)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.SkulabsSyncEnabled))
        {
            logger.LogDebug(
                "{Flag} is disabled. SkuLabs item sync fired but is doing nothing.",
                FeatureFlags.SkulabsSyncEnabled);
            return;
        }

        var result = await skulabsItemSyncService.Sync(cancellationToken);

        var affected = result.CreatedSkulabsItemIds.Concat(result.UpdatedSkulabsItemIds);
        await messageBus.PublishBatch(
            affected.Select(id => new SkulabsProductImportedEvent(id)), cancellationToken);

        logger.LogInformation(
            "SkuLabs item sync: Created: {Created}, Re-linked: {Updated}, Unmatched: {Unmatched}, Skipped: {Skipped}, "
            + "Ambiguous +{AmbCreated}/~{AmbUpdated}/-{AmbRemoved}.",
            result.CreatedSkulabsItemIds.Count, result.UpdatedSkulabsItemIds.Count,
            result.UnmatchedCount, result.SkippedCount,
            result.AmbiguousCreatedCount, result.AmbiguousUpdatedCount, result.AmbiguousRemovedCount);
    }
}
