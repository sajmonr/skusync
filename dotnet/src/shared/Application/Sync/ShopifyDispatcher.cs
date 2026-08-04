using System.Linq.Expressions;
using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Sync;

/// <summary>
/// Drains variants marked <see cref="ShopifyProductVariantEntity.PendingShopifySync"/> to Shopify,
/// batching one <c>productVariantsBulkUpdate</c> mutation per product. On success the pending flag
/// is cleared and the failure counter reset; on failure the variant stays pending, the counter is
/// incremented, and at <see cref="MaxFailedShopifySyncAttempts"/> consecutive failures the variant
/// is deactivated (excluded from all sync work) with an audit event. The
/// <see cref="FeatureFlags.ShopifyWriteBack"/> kill switch is checked here and nowhere else.
/// </summary>
public class ShopifyDispatcher(
    ApplicationDbContext dbContext,
    IShopifyProductService shopifyProductService,
    IFeatureManager featureManager,
    ILogger<ShopifyDispatcher> logger) : IShopifyDispatcher
{
    /// <summary>
    /// Maximum consecutive Shopify push failures tolerated for a single variant before it is
    /// marked <see cref="ShopifyProductVariantEntity.IsActive"/>=<c>false</c> and excluded from
    /// future syncs. Three was chosen so a one-off transient failure doesn't deactivate a row
    /// but a permanently broken target (e.g. the underlying Shopify product was deleted) stops
    /// being retried after a few dispatch cycles.
    /// </summary>
    private const int MaxFailedShopifySyncAttempts = 3;

    public Task<DispatchResult> DispatchAll(CancellationToken cancellationToken = default) =>
        Dispatch(variant => true, cancellationToken);

    public Task<DispatchResult> DispatchVariants(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default) =>
        variantIds.Count == 0
            ? Task.FromResult(DispatchResult.Empty)
            : Dispatch(variant => variantIds.Contains(variant.ShopifyProductVariantId), cancellationToken);

    private async Task<DispatchResult> Dispatch(
        Expression<Func<ShopifyProductVariantEntity, bool>> scope,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.ShopifyProductVariants
            .Where(scope)
            .Where(variant => variant.PendingShopifySync && variant.IsActive && !variant.IsDeleted)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return DispatchResult.Empty;
        }

        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack))
        {
            logger.LogInformation(
                "{Flag} is disabled. {Count} variant(s) remain pending a Shopify push.",
                FeatureFlags.ShopifyWriteBack, pending.Count);
            return new DispatchResult(Pending: pending.Count, Pushed: 0, Failed: 0);
        }

        var pushed = 0;

        foreach (var group in pending.GroupBy(variant => variant.GlobalProductId))
        {
            var productId = group.Key;
            var variants = group.ToArray();

            bool success;
            try
            {
                var batch = variants
                    .Select(variant => new ShopifyUpdateProductVariant(
                        variant.GlobalVariantId, variant.Sku, variant.Barcode))
                    .ToArray();

                logger.LogDebug(
                    "Dispatching {VariantCount} pending variant(s) to Shopify product {ProductId}.",
                    batch.Length, productId);

                success = await shopifyProductService.UpdateVariants(productId, batch);
            }
            catch (Exception exception)
            {
                // Treat exceptions like a structured "false": log and skip this product group so a
                // transient failure on one product doesn't abort the rest of the run.
                logger.LogError(
                    exception,
                    "Shopify update threw for product {ProductId}. {VariantCount} variant(s) stay pending and will be retried.",
                    productId, variants.Length);
                RecordFailedAttempt(variants);
                continue;
            }

            if (!success)
            {
                logger.LogError(
                    "Shopify rejected the push for product {ProductId}. {VariantCount} variant(s) stay pending and will be retried.",
                    productId, variants.Length);
                RecordFailedAttempt(variants);
                continue;
            }

            foreach (var variant in variants)
            {
                variant.PendingShopifySync = false;
                variant.FailedShopifySyncAttempts = 0;
                variant.UpdatedOnUtc = DateTime.UtcNow;
                pushed++;
            }
        }

        // SaveChanges runs unconditionally: even when nothing pushed we may have bumped
        // FailedShopifySyncAttempts or flipped IsActive on failed variants.
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Shopify dispatch done. Pushed {Pushed} of {Pending} pending variant(s).",
            pushed, pending.Count);

        return new DispatchResult(
            Pending: pending.Count,
            Pushed: pushed,
            Failed: pending.Count - pushed);
    }

    /// <summary>
    /// Increments the failed-attempt counter on every variant in the failed group and deactivates
    /// any that cross <see cref="MaxFailedShopifySyncAttempts"/>, writing an audit event for each.
    /// </summary>
    private void RecordFailedAttempt(IReadOnlyList<ShopifyProductVariantEntity> variants)
    {
        foreach (var variant in variants)
        {
            variant.FailedShopifySyncAttempts++;
            variant.UpdatedOnUtc = DateTime.UtcNow;

            if (variant.IsActive && variant.FailedShopifySyncAttempts >= MaxFailedShopifySyncAttempts)
            {
                variant.IsActive = false;
                logger.LogWarning(
                    "Variant {VariantId} deactivated after {FailedAttempts} consecutive failed Shopify sync attempts. It will be excluded from future syncs.",
                    variant.ShopifyProductVariantId, variant.FailedShopifySyncAttempts);
                dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    ShopifyProductVariantId = variant.ShopifyProductVariantId,
                    Message = VariantLogMessages.DeactivatedAfterFailedShopifySyncs(variant.FailedShopifySyncAttempts)
                });
            }
        }
    }
}
