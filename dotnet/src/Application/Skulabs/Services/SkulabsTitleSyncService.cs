using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Skulabs.Services;

/// <summary>
/// Treats the locally-stored Shopify variant <c>DisplayName</c> as the authoritative source
/// for a linked SkuLabs item's title. When SkuLabs disagrees this service pushes the variant
/// value up to SkuLabs (batched in a single <c>bulk_upsert</c> call) and mirrors the new
/// value into the local <see cref="SkulabsItemEntity.Title"/> alongside a human-readable log
/// event so the correction is visible in the variant history.
/// </summary>
/// <remarks>
/// The <see cref="FeatureFlags.SkulabsWriteBack"/> feature flag is consulted around the
/// outbound SkuLabs mutation. When the flag is disabled no SkuLabs HTTP call is made and the
/// local row is left untouched — the next sweep (or a later event after the flag is
/// re-enabled) will pick the drift up again because the comparison is direct.
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
            "Scanning database for SkuLabs-linked variants whose SkuLabs title has drifted from the variant display name.");

        var candidates = await dbContext.SkulabsItems
            .Include(item => item.ShopifyProductVariant)
            .Where(item => item.ShopifyProductVariant != null
                           && item.ShopifyProductVariant.DisplayName != item.Title)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            logger.LogDebug("No SkuLabs title drift detected.");
            return SkulabsTitleSyncResult.Empty;
        }

        logger.LogInformation(
            "Title drift sweep found {Candidates} SkuLabs-linked variant(s) needing a title push.",
            candidates.Count);

        return await Correct(candidates, cancellationToken);
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
        if (IsInSync(item))
        {
            logger.LogDebug(
                "SkuLabs item {SkulabsItemId} title already matches variant {VariantId}. No push needed.",
                item.SkulabsItemId, item.ShopifyProductVariantId);
            return new SkulabsTitleSyncResult(Checked: 1, Drifted: 0, Corrected: 0, Failed: 0);
        }

        logger.LogInformation(
            "SkuLabs item {SkulabsItemId} title needs correction on variant {VariantId}.",
            item.SkulabsItemId, item.ShopifyProductVariantId);

        return await Correct([item], cancellationToken);
    }

    /// <summary>
    /// Pushes the variant <c>DisplayName</c> up to SkuLabs for every supplied item via a single
    /// <c>bulk_upsert</c> call, then mirrors the new title into the local row and writes a
    /// variant log event for each corrected item.
    /// <list type="bullet">
    ///   <item><description>If <see cref="FeatureFlags.SkulabsWriteBack"/> is <b>disabled</b>: no SkuLabs call is made and no local mutation happens — the next sweep will detect the same drift again.</description></item>
    ///   <item><description>If the SkuLabs call throws: nothing is mirrored locally; all items in the batch are counted as failed and retried on the next run.</description></item>
    /// </list>
    /// A single <see cref="ApplicationDbContext.SaveChangesAsync(CancellationToken)"/> is issued at the end.
    /// </summary>
    private async Task<SkulabsTitleSyncResult> Correct(
        IReadOnlyList<SkulabsItemEntity> driftedItems,
        CancellationToken cancellationToken)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.SkulabsWriteBack))
        {
            logger.LogInformation(
                "{Flag} is disabled. {Count} SkuLabs title push(es) skipped; the next sweep will retry.",
                FeatureFlags.SkulabsWriteBack, driftedItems.Count);
            return new SkulabsTitleSyncResult(
                Checked: driftedItems.Count,
                Drifted: driftedItems.Count,
                Corrected: 0,
                Failed: driftedItems.Count);
        }

        var updates = driftedItems
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
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "SkuLabs bulk_upsert threw for {Count} title correction(s). Local rows left untouched — the next run will retry.",
                updates.Length);
            return new SkulabsTitleSyncResult(
                Checked: driftedItems.Count,
                Drifted: driftedItems.Count,
                Corrected: 0,
                Failed: driftedItems.Count);
        }

        foreach (var item in driftedItems)
        {
            ApplyCorrectionLocally(item);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "SkuLabs title sync done. Corrected {Count} item(s).",
            driftedItems.Count);

        return new SkulabsTitleSyncResult(
            Checked: driftedItems.Count,
            Drifted: driftedItems.Count,
            Corrected: driftedItems.Count,
            Failed: 0);
    }

    private void ApplyCorrectionLocally(SkulabsItemEntity item)
    {
        var variant = item.ShopifyProductVariant!;
        var oldTitle = item.Title;
        item.Title = variant.DisplayName;

        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = variant.ShopifyProductVariantId,
            Message = VariantLogMessages.SkulabsTitleSyncedFromVariant(oldTitle, variant.DisplayName)
        });

        variant.UpdatedOnUtc = DateTime.UtcNow;
    }

    private static bool IsInSync(SkulabsItemEntity item) =>
        item.ShopifyProductVariant is not null
        && string.Equals(item.ShopifyProductVariant.DisplayName, item.Title, StringComparison.Ordinal);
}
