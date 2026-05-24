using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Skulabs.Services;

/// <summary>
/// Treats the linked SkuLabs item as the authoritative source for a Shopify variant's
/// SKU and barcode. When the locally-stored Shopify values disagree with SkuLabs, this
/// service pushes the SkuLabs values back to Shopify (per-product, batched) and mirrors
/// the new values into the local <see cref="ShopifyProductVariantEntity"/> alongside a
/// human-readable log event so the correction is visible in the variant history.
/// </summary>
/// <remarks>
/// The <see cref="FeatureFlags.ShopifyWriteBack"/> feature flag is consulted only around
/// the outbound Shopify mutation. When the flag is disabled the Shopify call is skipped
/// but the local database mirror and variant log are still updated, and the variant is
/// marked <see cref="ShopifyProductVariantEntity.PendingShopifySync"/> so a later sweep
/// — once the flag is re-enabled — pushes the values to Shopify and clears the marker.
/// </remarks>
public class SkuAndBarcodeSyncService(
    ApplicationDbContext dbContext,
    IShopifyProductService shopifyProductService,
    IFeatureManager featureManager,
    ILogger<SkuAndBarcodeSyncService> logger) : ISkuAndBarcodeSyncService
{
    public async Task<SkuAndBarcodeSyncResult> SyncAll(CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Scanning database for SkuLabs-linked variants with drifted SKU/barcode or a pending Shopify push.");

        // "Drifted" = DB variant ≠ SkuLabs item. "Pending" = a previous correction couldn't be
        // pushed to Shopify (flag was off, or Shopify hasn't been reconciled yet). Both states
        // need a Shopify write; pending-only rows are already locally consistent with SkuLabs.
        var candidates = await dbContext.SkulabsItems
            .Include(item => item.ShopifyProductVariant)
            .Where(item => item.ShopifyProductVariant != null
                           && (item.ShopifyProductVariant.Sku != item.Sku
                               || item.ShopifyProductVariant.Barcode != item.Barcode
                               || item.ShopifyProductVariant.PendingShopifySync))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            logger.LogDebug("No drift detected and no pending Shopify pushes outstanding.");
            return SkuAndBarcodeSyncResult.Empty;
        }

        var driftedCount = candidates.Count(IsDrifted);
        logger.LogInformation(
            "Drift sweep found {Candidates} variant(s) needing attention ({Drifted} drifted, {Pending} pending-only Shopify push).",
            candidates.Count, driftedCount, candidates.Count - driftedCount);

        var corrected = await CorrectDrifted(candidates, cancellationToken);
        return new SkuAndBarcodeSyncResult(
            Checked: candidates.Count,
            Drifted: driftedCount,
            Corrected: corrected,
            Failed: candidates.Count - corrected);
    }

    public async Task<SkuAndBarcodeSyncResult> SyncForSkulabsItem(
        Guid skulabsItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await dbContext.SkulabsItems
            .Include(i => i.ShopifyProductVariant)
            .SingleOrDefaultAsync(i => i.SkulabsItemId == skulabsItemId, cancellationToken);

        if (item is null)
        {
            logger.LogWarning(
                "SkuLabs item {SkulabsItemId} was requested for drift check but no longer exists in the database.",
                skulabsItemId);
            return SkuAndBarcodeSyncResult.Empty;
        }

        if (item.ShopifyProductVariant is null)
        {
            logger.LogWarning(
                "SkuLabs item {SkulabsItemId} has no linked Shopify variant. Nothing to compare.",
                skulabsItemId);
            return SkuAndBarcodeSyncResult.Empty;
        }

        var drifted = IsDrifted(item);
        var pending = item.ShopifyProductVariant.PendingShopifySync;
        if (!drifted && !pending)
        {
            logger.LogDebug(
                "SkuLabs item {SkulabsItemId} is already in sync with its Shopify variant. No correction needed.",
                skulabsItemId);
            return new SkuAndBarcodeSyncResult(Checked: 1, Drifted: 0, Corrected: 0, Failed: 0);
        }

        logger.LogInformation(
            "SkuLabs item {SkulabsSourceItemId} needs correction on Shopify variant {ShopifyVariantId}: {Reason}.",
            item.SkulabsSourceItemId, item.ShopifyProductVariant.GlobalVariantId, DescribeCorrectionReason(item, pending));

        var corrected = await CorrectDrifted([item], cancellationToken);
        return new SkuAndBarcodeSyncResult(
            Checked: 1,
            Drifted: drifted ? 1 : 0,
            Corrected: corrected,
            Failed: 1 - corrected);
    }

    /// <summary>
    /// Applies the SkuLabs values to every supplied item — both to Shopify (when the
    /// <see cref="FeatureFlags.ShopifyWriteBack"/> feature flag is enabled) and to the local
    /// database. Items are grouped by Shopify product id so we issue at most one
    /// <see cref="IShopifyProductService.UpdateVariants"/> call per product per run.
    /// <list type="bullet">
    ///   <item><description>If the flag is <b>enabled</b> and Shopify accepts the update: local row is corrected, <see cref="ShopifyProductVariantEntity.PendingShopifySync"/> is cleared, log events are written.</description></item>
    ///   <item><description>If the flag is <b>enabled</b> but Shopify rejects the update: the entire product group is skipped — local row and pending flag are left untouched so the next run retries.</description></item>
    ///   <item><description>If the flag is <b>disabled</b>: no Shopify call is made; local row is corrected, <see cref="ShopifyProductVariantEntity.PendingShopifySync"/> is set to <c>true</c>, log events are written so the eventual push when the flag is re-enabled has the right values.</description></item>
    /// </list>
    /// A single <see cref="ApplicationDbContext.SaveChangesAsync(CancellationToken)"/> is issued at the end.
    /// </summary>
    /// <returns>The number of items whose local row reached the desired state in this run (whether or not Shopify was actually written).</returns>
    private async Task<int> CorrectDrifted(
        IReadOnlyList<SkulabsItemEntity> driftedItems,
        CancellationToken cancellationToken)
    {
        var shopifyWriteBackEnabled = await featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack);
        if (!shopifyWriteBackEnabled)
        {
            logger.LogInformation(
                "{Flag} is disabled. Drift will be corrected in the local database and flagged for a later Shopify push.",
                FeatureFlags.ShopifyWriteBack);
        }

        var correctedCount = 0;

        var byProduct = driftedItems.GroupBy(item => item.ShopifyProductVariant!.GlobalProductId);
        foreach (var group in byProduct)
        {
            var productId = group.Key;
            var items = group.ToArray();

            if (shopifyWriteBackEnabled)
            {
                bool success;
                try
                {
                    var updateBatch = items
                        .Select(item => new ShopifyUpdateProductVariant(
                            item.ShopifyProductVariant!.GlobalVariantId,
                            item.Sku,
                            item.Barcode))
                        .ToArray();

                    logger.LogDebug(
                        "Pushing {VariantCount} drift/pending correction(s) to Shopify product {ProductId}.",
                        updateBatch.Length, productId);

                    success = await shopifyProductService.UpdateVariants(productId, updateBatch);
                }
                catch (Exception exception)
                {
                    // Treat exceptions the same as a structured "false" — log and skip this
                    // product group so a transient failure on one product doesn't abort the
                    // entire sweep. The next run will re-attempt these items.
                    logger.LogError(
                        exception,
                        "Shopify update threw for product {ProductId}. {VariantCount} variant(s) will be retried on the next run — local rows left untouched.",
                        productId, items.Length);
                    continue;
                }

                if (!success)
                {
                    logger.LogError(
                        "Shopify rejected drift correction for product {ProductId}. {VariantCount} variant(s) will be retried on the next run — local rows left untouched.",
                        productId, items.Length);
                    continue;
                }

                foreach (var item in items)
                {
                    ApplyCorrectionLocally(item);
                    item.ShopifyProductVariant!.PendingShopifySync = false;
                    correctedCount++;
                }
            }
            else
            {
                foreach (var item in items)
                {
                    ApplyCorrectionLocally(item);
                    item.ShopifyProductVariant!.PendingShopifySync = true;
                    correctedCount++;
                }
            }
        }

        if (correctedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Drift sync done. Corrected {CorrectedCount} of {Candidates} candidate variant(s).",
            correctedCount, driftedItems.Count);

        return correctedCount;
    }

    /// <summary>
    /// Mirrors the SkuLabs values into the local variant row and records one log event per
    /// changed field (SKU and/or barcode). When the row is already locally in sync (a
    /// pending-only candidate) this is a no-op apart from bumping <c>UpdatedOnUtc</c>.
    /// </summary>
    private void ApplyCorrectionLocally(SkulabsItemEntity item)
    {
        var variant = item.ShopifyProductVariant!;

        if (!string.Equals(variant.Sku, item.Sku, StringComparison.Ordinal))
        {
            var oldSku = variant.Sku;
            variant.Sku = item.Sku;
            AddVariantLog(variant.ShopifyProductVariantId, VariantLogMessages.SkuCorrectedFromSkulabs(oldSku, item.Sku));
        }

        if (!string.Equals(variant.Barcode, item.Barcode, StringComparison.Ordinal))
        {
            var oldBarcode = variant.Barcode;
            variant.Barcode = item.Barcode;
            AddVariantLog(variant.ShopifyProductVariantId, VariantLogMessages.BarcodeCorrectedFromSkulabs(oldBarcode, item.Barcode));
        }

        variant.UpdatedOnUtc = DateTime.UtcNow;
    }

    private void AddVariantLog(Guid variantGuid, string message)
    {
        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = variantGuid,
            Message = message
        });
    }

    private static bool IsDrifted(SkulabsItemEntity item) =>
        !string.Equals(item.ShopifyProductVariant!.Sku, item.Sku, StringComparison.Ordinal)
        || !string.Equals(item.ShopifyProductVariant!.Barcode, item.Barcode, StringComparison.Ordinal);

    private static string DescribeCorrectionReason(SkulabsItemEntity item, bool pending)
    {
        var variant = item.ShopifyProductVariant!;
        var reasons = new List<string>();

        if (!string.Equals(variant.Sku, item.Sku, StringComparison.Ordinal))
        {
            reasons.Add($"SKU '{variant.Sku}' → '{item.Sku}'");
        }

        if (!string.Equals(variant.Barcode, item.Barcode, StringComparison.Ordinal))
        {
            reasons.Add($"barcode '{variant.Barcode}' → '{item.Barcode}'");
        }

        if (pending)
        {
            reasons.Add("pending Shopify push from a previous run");
        }

        return string.Join("; ", reasons);
    }
}
