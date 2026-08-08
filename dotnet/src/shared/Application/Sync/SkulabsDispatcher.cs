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
/// <para>
/// Items are reached through the listing table filtered by <see cref="SkulabsItemLinks.IsSyncable"/>,
/// so an item sharing its variant with another item is never pushed.
/// </para>
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
        Dispatch(listing => true, cancellationToken);

    public Task<DispatchResult> DispatchVariants(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default) =>
        variantIds.Count == 0
            ? Task.FromResult(DispatchResult.Empty)
            : Dispatch(
                listing => listing.ShopifyProductVariantId.HasValue
                           && variantIds.Contains(listing.ShopifyProductVariantId.Value),
                cancellationToken);

    private async Task<DispatchResult> Dispatch(
        Expression<Func<SkulabsItemListingEntity, bool>> scope,
        CancellationToken cancellationToken)
    {
        var pendingLinks = await dbContext.SkulabsItemListings
            .Include(listing => listing.SkulabsItem)
            .Include(listing => listing.ShopifyProductVariant)
            .ThenInclude(variant => variant!.DesiredState)
            .Where(SkulabsItemLinks.IsSyncable)
            .Where(scope)
            .Where(listing => listing.SkulabsItem!.PendingSkulabsSync
                              && listing.SkulabsItem.FailedSkulabsSyncAttempts < MaxFailedSkulabsSyncAttempts
                              && listing.ShopifyProductVariant!.IsActive
                              && !listing.ShopifyProductVariant.IsDeleted
                              && listing.ShopifyProductVariant.DesiredState != null)
            .ToListAsync(cancellationToken);

        var pending = pendingLinks.Select(listing => listing.SkulabsItem!).ToList();

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

        // Keyed off the link rather than the item so each update carries the desired state decided
        // for that link's variant, which is where the values live.
        var updates = pendingLinks
            .Select(link => new SkulabsItemUpdateWithId(
                link.SkulabsItem!.SkulabsSourceItemId,
                link.ShopifyProductVariant!.DesiredState!.Title))
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
        catch (SkulabsRequestFailedException failure) when (failure.IsCredentialFailure)
        {
            // Our credentials, not these items. Every batch would fail identically, so counting
            // strikes here would exclude the whole catalogue within a few cycles over a problem one
            // credential fix resolves — and an operator would then have to unpick the exclusions by
            // hand. Leave the rows pending and make the real cause loud instead.
            logger.LogError(
                failure,
                "SkuLabs rejected the push for {Count} item(s) with {StatusCode} — a credentials or "
                + "permissions problem, not a problem with these items. They stay pending with their "
                + "failure counters untouched. TraceId: {SkulabsTraceId}.",
                updates.Length, (int)failure.StatusCode, failure.SkulabsTraceId);
            return new DispatchResult(Pending: pending.Count, Pushed: 0, Failed: pending.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "SkuLabs bulk_upsert threw for {Count} pending item(s). Items stay pending and will be retried.",
                updates.Length);
            RecordFailedAttempt(pendingLinks);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new DispatchResult(Pending: pending.Count, Pushed: 0, Failed: pending.Count);
        }

        foreach (var link in pendingLinks)
        {
            var item = link.SkulabsItem!;

            // SkuLabs acknowledges without echoing state, so the mirror advances to what we sent
            // rather than to what it reported. That makes the write provisional: the next item sync
            // replaces it with a real observation, and any normalisation SkuLabs applied shows up
            // then. Not advancing it at all would leave the item permanently unequal to its desired
            // state and re-push on every cycle until that sync arrived.
            item.Title = link.ShopifyProductVariant!.DesiredState!.Title;
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
    private void RecordFailedAttempt(IReadOnlyList<SkulabsItemListingEntity> links)
    {
        foreach (var link in links)
        {
            var item = link.SkulabsItem!;
            item.FailedSkulabsSyncAttempts++;

            if (item.FailedSkulabsSyncAttempts >= MaxFailedSkulabsSyncAttempts)
            {
                logger.LogWarning(
                    "SkuLabs item {SkulabsItemId} excluded from dispatch after {FailedAttempts} consecutive failed push attempts.",
                    item.SkulabsItemId, item.FailedSkulabsSyncAttempts);
                dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    ShopifyProductVariantId = link.ShopifyProductVariantId!.Value,
                    Message = VariantLogMessages.SkulabsItemExcludedAfterFailedSyncs(item.FailedSkulabsSyncAttempts)
                });
            }
        }
    }
}
