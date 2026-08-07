using Application.Skulabs.Services;
using Application.Sync;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Jobs;

/// <summary>
/// Entry points for the scheduled sync-pipeline jobs, invoked by Hangfire recurring jobs. Each
/// method is an independent recurring job (staggered by cron), so one failing run never blocks the
/// others. The Shopify product import is scheduled directly against
/// <see cref="Application.Products.Services.IProductsService"/> and so has no method here.
/// </summary>
public class RecurringJobs(
    ISkulabsItemSyncService skulabsItemSyncService,
    IReconciler reconciler,
    IShopifyDispatcher shopifyDispatcher,
    ISkulabsDispatcher skulabsDispatcher,
    IFeatureManager featureManager,
    ILogger<RecurringJobs> logger)
{
    /// <summary>
    /// Pulls every SkuLabs item, links it to the matching Shopify variant, surfaces the ones that
    /// cannot be cleanly mapped, and reconciles every created or re-linked pair so any drift is
    /// mirrored locally and marked pending. Gated by the <see cref="FeatureFlags.SkulabsSyncEnabled"/>
    /// flag — when disabled the sweep is a no-op. The reconciler loads all state into memory and
    /// saves once, so two overlapping runs would race; the lock keeps this recurring job
    /// single-flight (the manual full sync guards itself the same way).
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

        logger.LogInformation(
            "SkuLabs item sync: Created: {Created}, Re-linked: {Updated}, Removed: {Removed}, "
            + "Unresolved listings: {Unresolved}, Skipped: {Skipped}, Ambiguous: {Ambiguous}.",
            result.CreatedSkulabsItemIds.Count, result.UpdatedSkulabsItemIds.Count,
            result.RemovedCount, result.UnresolvedListingCount, result.SkippedCount,
            result.AmbiguousCount);

        var touched = result.CreatedSkulabsItemIds.Concat(result.UpdatedSkulabsItemIds).ToArray();
        var reconciled = await reconciler.ReconcileSkulabsItems(touched, cancellationToken);

        logger.LogInformation(
            "Post-link reconcile: {VariantsMarked} variant(s) and {ItemsMarked} item(s) marked pending.",
            reconciled.VariantsMarked, reconciled.ItemsMarked);
    }

    /// <summary>
    /// Reconciles every linked variant/item pair — the nightly safety net that catches anything
    /// the inline per-scope reconciles missed. Marks drifted rows pending; the dispatchers push
    /// them on their own schedule.
    /// </summary>
    public async Task ReconcileAll(CancellationToken cancellationToken = default)
    {
        var result = await reconciler.ReconcileAll(cancellationToken);

        logger.LogInformation(
            "Full reconcile: {VariantsMarked} variant(s) marked pending Shopify push, {ItemsMarked} item(s) marked pending SkuLabs push.",
            result.VariantsMarked, result.ItemsMarked);
    }

    /// <summary>
    /// Drains pending variants to Shopify. Gated by <see cref="FeatureFlags.ShopifyAutoDispatch"/> —
    /// when disabled, dirty variants accumulate as pending until a manual sync pushes them.
    /// </summary>
    public async Task DispatchShopify(CancellationToken cancellationToken = default)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifyAutoDispatch))
        {
            logger.LogDebug(
                "{Flag} is disabled. Shopify dispatch fired but is doing nothing.",
                FeatureFlags.ShopifyAutoDispatch);
            return;
        }

        var result = await shopifyDispatcher.DispatchAll(cancellationToken);

        if (result.Pending > 0)
        {
            logger.LogInformation(
                "Shopify dispatch: Pending: {Pending}, Pushed: {Pushed}, Failed: {Failed}.",
                result.Pending, result.Pushed, result.Failed);
        }
    }

    /// <summary>
    /// Drains pending items to SkuLabs. Gated by <see cref="FeatureFlags.SkulabsAutoDispatch"/> —
    /// when disabled, dirty items accumulate as pending until a manual sync pushes them.
    /// </summary>
    public async Task DispatchSkulabs(CancellationToken cancellationToken = default)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.SkulabsAutoDispatch))
        {
            logger.LogDebug(
                "{Flag} is disabled. SkuLabs dispatch fired but is doing nothing.",
                FeatureFlags.SkulabsAutoDispatch);
            return;
        }

        var result = await skulabsDispatcher.DispatchAll(cancellationToken);

        if (result.Pending > 0)
        {
            logger.LogInformation(
                "SkuLabs dispatch: Pending: {Pending}, Pushed: {Pushed}, Failed: {Failed}, RateLimited: {RateLimited}.",
                result.Pending, result.Pushed, result.Failed, result.RateLimited);
        }
    }
}
