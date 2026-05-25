using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.RateLimiting;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Skulabs.Services;

/// <summary>
/// Treats the locally-stored Shopify variant <c>DisplayName</c> as the authoritative source
/// for a linked SkuLabs item's title. When SkuLabs disagrees this service mirrors the
/// variant value into the local <see cref="SkulabsItemEntity.Title"/> alongside a
/// human-readable log event, and pushes the new value up to SkuLabs in a single
/// <c>bulk_upsert</c> call.
/// </summary>
/// <remarks>
/// The <see cref="FeatureFlags.SkulabsWriteBack"/> feature flag is consulted only around
/// the outbound SkuLabs HTTP call. When the flag is disabled the local row is still
/// mirrored and the log event is still written, and the item is marked
/// <see cref="SkulabsItemEntity.PendingSkulabsSync"/> so a later sweep — once the flag is
/// re-enabled — pushes the value to SkuLabs and clears the marker.
/// </remarks>
public class SkulabsTitleSyncService(
    ApplicationDbContext dbContext,
    ISkulabsItemClient skulabsItemClient,
    IFeatureManager featureManager,
    ILogger<SkulabsTitleSyncService> logger) : ISkulabsTitleSyncService
{
    public async Task<SkulabsTitleSyncResult> SyncAll(CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Scanning database for SkuLabs-linked variants with drifted titles or a pending SkuLabs push.");

        // "Drifted" = DB variant DisplayName ≠ SkuLabs item Title. "Pending" = a previous
        // correction couldn't be pushed to SkuLabs (flag was off). Both states need a SkuLabs
        // write; pending-only rows are already locally consistent with the variant.
        var candidates = await dbContext.SkulabsItems
            .Include(item => item.ShopifyProductVariant)
            .Where(item => item.ShopifyProductVariant != null
                           && (item.ShopifyProductVariant.DisplayName != item.Title
                               || item.PendingSkulabsSync))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            logger.LogDebug("No SkuLabs title drift detected and no pending SkuLabs pushes outstanding.");
            return SkulabsTitleSyncResult.Empty;
        }

        var driftedCount = candidates.Count(IsDrifted);
        logger.LogInformation(
            "Title drift sweep found {Candidates} SkuLabs-linked variant(s) needing a push ({Drifted} drifted, {Pending} pending-only).",
            candidates.Count, driftedCount, candidates.Count - driftedCount);

        return await Correct(candidates, driftedCount, cancellationToken);
    }

    public async Task<SkulabsTitleSyncResult> SyncForVariant(Guid variantId, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.SkulabsItems
            .Include(i => i.ShopifyProductVariant)
            .SingleOrDefaultAsync(i => i.ShopifyProductVariantId == variantId, cancellationToken);

        if (item is null)
        {
            logger.LogDebug(
                "Variant {VariantId} has no linked SkuLabs item. Nothing to push.",
                variantId);
            return SkulabsTitleSyncResult.Empty;
        }

        return await CorrectSingle(item, cancellationToken);
    }

    public async Task<SkulabsTitleSyncResult> SyncForSkulabsItem(
        Guid skulabsItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await dbContext.SkulabsItems
            .Include(i => i.ShopifyProductVariant)
            .SingleOrDefaultAsync(i => i.SkulabsItemId == skulabsItemId, cancellationToken);

        if (item is null)
        {
            logger.LogWarning(
                "SkuLabs item {SkulabsItemId} was requested for title sync but no longer exists in the database.",
                skulabsItemId);
            return SkulabsTitleSyncResult.Empty;
        }

        if (item.ShopifyProductVariant is null)
        {
            logger.LogWarning(
                "SkuLabs item {SkulabsItemId} has no linked Shopify variant. Nothing to compare.",
                skulabsItemId);
            return SkulabsTitleSyncResult.Empty;
        }

        return await CorrectSingle(item, cancellationToken);
    }

    private async Task<SkulabsTitleSyncResult> CorrectSingle(
        SkulabsItemEntity item,
        CancellationToken cancellationToken)
    {
        var drifted = IsDrifted(item);
        var pending = item.PendingSkulabsSync;

        if (!drifted && !pending)
        {
            logger.LogDebug(
                "SkuLabs item {SkulabsItemId} title already matches variant {VariantId} and has no pending push. No correction needed.",
                item.SkulabsItemId, item.ShopifyProductVariantId);
            return new SkulabsTitleSyncResult(Checked: 1, Drifted: 0, Corrected: 0, Failed: 0);
        }

        logger.LogInformation(
            "SkuLabs item {SkulabsItemId} title needs correction on variant {VariantId}: '{OldTitle}' → '{NewTitle}' (drifted: {Drifted}, pending: {Pending}).",
            item.SkulabsItemId,
            item.ShopifyProductVariantId,
            item.Title,
            item.ShopifyProductVariant!.DisplayName,
            drifted,
            pending);

        return await Correct([item], drifted ? 1 : 0, cancellationToken);
    }

    /// <summary>
    /// Pushes the variant <c>DisplayName</c> up to SkuLabs for every supplied item via a single
    /// <c>bulk_upsert</c> call, mirrors any drifted titles into the local row, and writes a
    /// variant log event for each title change. The <see cref="SkulabsItemEntity.PendingSkulabsSync"/>
    /// flag is set or cleared per item depending on whether the push actually happened.
    /// <list type="bullet">
    ///   <item><description>If <see cref="FeatureFlags.SkulabsWriteBack"/> is <b>enabled</b> and SkuLabs accepts the push: drifted rows are mirrored locally, <c>PendingSkulabsSync</c> is cleared on every item, log events are written for each title change.</description></item>
    ///   <item><description>If the flag is <b>enabled</b> but the SkuLabs call throws: nothing is mirrored locally and the pending flag is left as-is — every item in the batch is counted as failed and retried on the next run.</description></item>
    ///   <item><description>If the flag is <b>disabled</b>: no SkuLabs call is made; drifted rows are mirrored locally with <c>PendingSkulabsSync</c> set to <c>true</c>, log events are written for each title change so the eventual push when the flag is re-enabled has the right values. Pending-only candidates are left untouched.</description></item>
    /// </list>
    /// A single <see cref="ApplicationDbContext.SaveChangesAsync(CancellationToken)"/> is issued at the end.
    /// </summary>
    private async Task<SkulabsTitleSyncResult> Correct(
        IReadOnlyList<SkulabsItemEntity> candidates,
        int driftedCount,
        CancellationToken cancellationToken)
    {
        var writeBackEnabled = await featureManager.IsEnabledAsync(FeatureFlags.SkulabsWriteBack);

        if (writeBackEnabled)
        {
            var updates = candidates
                .Select(item => new SkulabsItemUpdateWithId(
                    item.SkulabsSourceItemId,
                    item.ShopifyProductVariant!.DisplayName))
                .ToArray();

            try
            {
                logger.LogDebug(
                    "Pushing {Count} SkuLabs title correction(s) via bulk_upsert.",
                    updates.Length);
                await skulabsItemClient.UpdateItems(updates);
            }
            catch (RateLimitedException rateLimited)
            {
                logger.LogWarning(
                    "Skipped {Count} SkuLabs title correction(s); SkuLabs is in rate-limit cooldown for {RetrySeconds}s. Local rows left untouched — the next run will retry.",
                    updates.Length,
                    rateLimited.RetryAfter.TotalSeconds);
                return new SkulabsTitleSyncResult(
                    Checked: candidates.Count,
                    Drifted: driftedCount,
                    Corrected: 0,
                    Failed: candidates.Count);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "SkuLabs bulk_upsert threw for {Count} title correction(s). Local rows left untouched — the next run will retry.",
                    updates.Length);
                return new SkulabsTitleSyncResult(
                    Checked: candidates.Count,
                    Drifted: driftedCount,
                    Corrected: 0,
                    Failed: candidates.Count);
            }

            foreach (var item in candidates)
            {
                ApplyCorrectionLocally(item);
                item.PendingSkulabsSync = false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "SkuLabs title sync done. Corrected {Count} item(s).",
                candidates.Count);

            return new SkulabsTitleSyncResult(
                Checked: candidates.Count,
                Drifted: driftedCount,
                Corrected: candidates.Count,
                Failed: 0);
        }

        logger.LogInformation(
            "{Flag} is disabled. {Drifted} drifted row(s) will be mirrored locally and marked pending; {PendingOnly} pending-only row(s) stay deferred until the flag is re-enabled.",
            FeatureFlags.SkulabsWriteBack, driftedCount, candidates.Count - driftedCount);

        var correctedCount = 0;
        foreach (var item in candidates)
        {
            if (!IsDrifted(item))
            {
                continue;
            }

            ApplyCorrectionLocally(item);
            item.PendingSkulabsSync = true;
            correctedCount++;
        }

        if (correctedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SkulabsTitleSyncResult(
            Checked: candidates.Count,
            Drifted: driftedCount,
            Corrected: correctedCount,
            Failed: 0);
    }

    private void ApplyCorrectionLocally(SkulabsItemEntity item)
    {
        var variant = item.ShopifyProductVariant!;

        if (string.Equals(variant.DisplayName, item.Title, StringComparison.Ordinal))
        {
            return;
        }

        var oldTitle = item.Title;
        item.Title = variant.DisplayName;

        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = variant.ShopifyProductVariantId,
            Message = VariantLogMessages.SkulabsTitleSyncedFromVariant(oldTitle, variant.DisplayName)
        });

        variant.UpdatedOnUtc = DateTime.UtcNow;
    }

    private static bool IsDrifted(SkulabsItemEntity item) =>
        item.ShopifyProductVariant is not null
        && !string.Equals(item.ShopifyProductVariant.DisplayName, item.Title, StringComparison.Ordinal);
}
