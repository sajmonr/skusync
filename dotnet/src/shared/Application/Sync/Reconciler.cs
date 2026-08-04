using System.Linq.Expressions;
using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Sync;

/// <summary>
/// Compares the local Shopify-variant and SkuLabs-item mirrors for linked pairs and corrects the
/// local rows per the field-authority rules, marking each corrected row pending a push. Every
/// correction writes a <see cref="VariantLogMessages"/> audit event so the change is visible in
/// the variant history.
/// </summary>
public class Reconciler(
    ApplicationDbContext dbContext,
    ILogger<Reconciler> logger) : IReconciler
{
    public Task<ReconcileResult> ReconcileAll(CancellationToken cancellationToken = default) =>
        Reconcile(item => true, cancellationToken);

    public Task<ReconcileResult> ReconcileVariants(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default) =>
        variantIds.Count == 0
            ? Task.FromResult(ReconcileResult.Empty)
            : Reconcile(item => variantIds.Contains(item.ShopifyProductVariantId), cancellationToken);

    public Task<ReconcileResult> ReconcileSkulabsItems(
        IReadOnlyCollection<Guid> skulabsItemIds,
        CancellationToken cancellationToken = default) =>
        skulabsItemIds.Count == 0
            ? Task.FromResult(ReconcileResult.Empty)
            : Reconcile(item => skulabsItemIds.Contains(item.SkulabsItemId), cancellationToken);

    private async Task<ReconcileResult> Reconcile(
        Expression<Func<SkulabsItemEntity, bool>> scope,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.SkulabsItems
            .Include(item => item.ShopifyProductVariant)
            .Where(scope)
            .Where(item => item.ShopifyProductVariant != null
                           && item.ShopifyProductVariant.IsActive
                           && !item.ShopifyProductVariant.IsDeleted
                           && ((item.Sku != "" && item.ShopifyProductVariant.Sku != item.Sku)
                               || (item.Barcode != "" && item.ShopifyProductVariant.Barcode != item.Barcode)
                               || item.ShopifyProductVariant.DisplayName != item.Title))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return ReconcileResult.Empty;
        }

        var variantsMarked = 0;
        var itemsMarked = 0;

        foreach (var item in candidates)
        {
            if (MirrorSkuAndBarcode(item))
            {
                item.ShopifyProductVariant!.PendingShopifySync = true;
                variantsMarked++;
            }

            if (MirrorTitle(item))
            {
                item.PendingSkulabsSync = true;
                itemsMarked++;
            }
        }

        if (variantsMarked > 0 || itemsMarked > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Reconcile pass corrected {VariantsMarked} variant(s) (pending Shopify push) and {ItemsMarked} item(s) (pending SkuLabs push) out of {Candidates} candidate pair(s).",
                variantsMarked, itemsMarked, candidates.Count);
        }

        return new ReconcileResult(variantsMarked, itemsMarked);
    }

    /// <summary>
    /// Mirrors the authoritative SkuLabs SKU/barcode into the variant when they drift. A blank
    /// SkuLabs value is never authoritative — SkuLabs simply has no value on record — so it never
    /// counts as drift and never erases a good variant value.
    /// </summary>
    private bool MirrorSkuAndBarcode(SkulabsItemEntity item)
    {
        var variant = item.ShopifyProductVariant!;
        var changed = false;

        if (!string.IsNullOrEmpty(item.Sku)
            && !string.Equals(variant.Sku, item.Sku, StringComparison.Ordinal))
        {
            var oldSku = variant.Sku;
            variant.Sku = item.Sku;
            AddVariantLog(variant.ShopifyProductVariantId, VariantLogMessages.SkuCorrectedFromSkulabs(oldSku, item.Sku));
            changed = true;
        }

        if (!string.IsNullOrEmpty(item.Barcode)
            && !string.Equals(variant.Barcode, item.Barcode, StringComparison.Ordinal))
        {
            var oldBarcode = variant.Barcode;
            variant.Barcode = item.Barcode;
            AddVariantLog(variant.ShopifyProductVariantId, VariantLogMessages.BarcodeCorrectedFromSkulabs(oldBarcode, item.Barcode));
            changed = true;
        }

        if (changed)
        {
            variant.UpdatedOnUtc = DateTime.UtcNow;
        }

        return changed;
    }

    /// <summary>
    /// Mirrors the authoritative variant <c>DisplayName</c> into the SkuLabs item title when they
    /// drift.
    /// </summary>
    private bool MirrorTitle(SkulabsItemEntity item)
    {
        var variant = item.ShopifyProductVariant!;

        if (string.Equals(variant.DisplayName, item.Title, StringComparison.Ordinal))
        {
            return false;
        }

        var oldTitle = item.Title;
        item.Title = variant.DisplayName;
        AddVariantLog(
            variant.ShopifyProductVariantId,
            VariantLogMessages.SkulabsTitleSyncedFromVariant(oldTitle, variant.DisplayName));
        variant.UpdatedOnUtc = DateTime.UtcNow;

        return true;
    }

    private void AddVariantLog(Guid variantGuid, string message)
    {
        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = variantGuid,
            Message = message
        });
    }
}
