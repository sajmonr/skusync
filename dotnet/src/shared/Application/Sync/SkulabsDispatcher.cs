using System.Linq.Expressions;
using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.RateLimiting;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Sync;

/// <summary>
/// Drains items marked <see cref="SkulabsItemEntity.PendingSkulabsSync"/> to SkuLabs in a single
/// <c>bulk_upsert</c>. On success the pending flags are cleared and the failure counters reset.
/// On failure every item in the batch stays pending with its counter incremented; at
/// <see cref="MaxFailedSkulabsSyncAttempts"/> consecutive failures an item is excluded from future
/// dispatch runs with an audit event, so a permanently rejected item cannot poison every batch.
/// A rate-limited run is not a failure — rows stay pending, counters untouched, and the run
/// reports the cooldown. The <see cref="FeatureFlags.SkulabsWriteBack"/> kill switch is checked
/// here and nowhere else.
/// </summary>
public class SkulabsDispatcher(
    ApplicationDbContext dbContext,
    ISkulabsItemClient skulabsItemClient,
    IFeatureManager featureManager,
    ILogger<SkulabsDispatcher> logger) : ISkulabsDispatcher
{
    /// <summary>
    /// Maximum consecutive SkuLabs push failures tolerated for a single item before it is excluded
    /// from future dispatch runs. Mirrors the Shopify dispatcher's deactivation threshold.
    /// </summary>
    private const int MaxFailedSkulabsSyncAttempts = 3;

    public Task<DispatchResult> DispatchAll(CancellationToken cancellationToken = default) =>
        Dispatch(item => true, cancellationToken);

    public Task<DispatchResult> DispatchVariants(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default) =>
        variantIds.Count == 0
            ? Task.FromResult(DispatchResult.Empty)
            : Dispatch(item => variantIds.Contains(item.ShopifyProductVariantId), cancellationToken);

    private async Task<DispatchResult> Dispatch(
        Expression<Func<SkulabsItemEntity, bool>> scope,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.SkulabsItems
            .Include(item => item.ShopifyProductVariant)
            .Where(scope)
            .Where(item => item.PendingSkulabsSync
                           && item.FailedSkulabsSyncAttempts < MaxFailedSkulabsSyncAttempts
                           && item.ShopifyProductVariant != null
                           && item.ShopifyProductVariant.IsActive
                           && !item.ShopifyProductVariant.IsDeleted)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return DispatchResult.Empty;
        }

        if (!await featureManager.IsEnabledAsync(FeatureFlags.SkulabsWriteBack))
        {
            logger.LogInformation(
                "{Flag} is disabled. {Count} item(s) remain pending a SkuLabs push.",
                FeatureFlags.SkulabsWriteBack, pending.Count);
            return new DispatchResult(Pending: pending.Count, Pushed: 0, Failed: 0);
        }

        var updates = pending
            .Select(item => new SkulabsItemUpdateWithId(item.SkulabsSourceItemId, item.Title))
            .ToArray();

        try
        {
            logger.LogDebug("Dispatching {Count} pending SkuLabs title(s) via bulk_upsert.", updates.Length);
            await skulabsItemClient.UpdateItems(updates);
        }
        catch (RateLimitedException rateLimited)
        {
            // Rate limiting means "later", not "broken" — rows stay pending, counters untouched.
            logger.LogWarning(
                "Skipped {Count} SkuLabs push(es); SkuLabs is in rate-limit cooldown for {RetrySeconds}s. Items stay pending and will be retried.",
                updates.Length, rateLimited.RetryAfter.TotalSeconds);
            return new DispatchResult(
                Pending: pending.Count,
                Pushed: 0,
                Failed: 0,
                RetryAfter: rateLimited.RetryAfter);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "SkuLabs bulk_upsert threw for {Count} pending item(s). Items stay pending and will be retried.",
                updates.Length);
            RecordFailedAttempt(pending);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new DispatchResult(Pending: pending.Count, Pushed: 0, Failed: pending.Count);
        }

        foreach (var item in pending)
        {
            item.PendingSkulabsSync = false;
            item.FailedSkulabsSyncAttempts = 0;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SkuLabs dispatch done. Pushed {Count} item(s).", pending.Count);

        return new DispatchResult(Pending: pending.Count, Pushed: pending.Count, Failed: 0);
    }

    /// <summary>
    /// Increments the failed-attempt counter on every item in the failed batch and writes an audit
    /// event for any item crossing <see cref="MaxFailedSkulabsSyncAttempts"/> — it is excluded
    /// from future runs by the pending query's counter bound.
    /// </summary>
    private void RecordFailedAttempt(IReadOnlyList<SkulabsItemEntity> items)
    {
        foreach (var item in items)
        {
            item.FailedSkulabsSyncAttempts++;

            if (item.FailedSkulabsSyncAttempts >= MaxFailedSkulabsSyncAttempts)
            {
                logger.LogWarning(
                    "SkuLabs item {SkulabsItemId} excluded from dispatch after {FailedAttempts} consecutive failed push attempts.",
                    item.SkulabsItemId, item.FailedSkulabsSyncAttempts);
                dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    ShopifyProductVariantId = item.ShopifyProductVariantId,
                    Message = VariantLogMessages.SkulabsItemExcludedAfterFailedSyncs(item.FailedSkulabsSyncAttempts)
                });
            }
        }
    }
}
