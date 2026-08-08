using System.Linq.Expressions;
using Application.Products.Services;
using Application.Sync.Merge;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Sync;

/// <inheritdoc cref="IReconciler"/>
public class Reconciler(
    ApplicationDbContext dbContext,
    MergeRuleChain mergeRules,
    ILogger<Reconciler> logger) : IReconciler
{
    public Task<ReconcileResult> ReconcileAll(CancellationToken cancellationToken = default) =>
        Reconcile(variant => true, MergeOrigin.Routine, cancellationToken);

    public Task<ReconcileResult> ReconcileVariants(
        IReadOnlyCollection<Guid> variantIds,
        MergeOrigin origin = MergeOrigin.Routine,
        CancellationToken cancellationToken = default) =>
        variantIds.Count == 0
            ? Task.FromResult(ReconcileResult.Empty)
            : Reconcile(variant => variantIds.Contains(variant.ShopifyProductVariantId), origin, cancellationToken);

    public Task<ReconcileResult> ReconcileSkulabsItems(
        IReadOnlyCollection<Guid> skulabsItemIds,
        CancellationToken cancellationToken = default) =>
        skulabsItemIds.Count == 0
            ? Task.FromResult(ReconcileResult.Empty)
            : Reconcile(
                variant => variant.SkulabsItemListings.Any(listing => skulabsItemIds.Contains(listing.SkulabsItemId)),
                MergeOrigin.Routine,
                cancellationToken);

    private async Task<ReconcileResult> Reconcile(
        Expression<Func<ShopifyProductVariantEntity, bool>> scope,
        MergeOrigin origin,
        CancellationToken cancellationToken)
    {
        // Deleted and deactivated variants are frozen: nothing may be decided for them, and merging
        // them would resurrect values the dispatchers have already given up on pushing.
        var candidates = await dbContext.ShopifyProductVariants
            .Where(scope)
            .Where(variant => variant.IsActive && !variant.IsDeleted)
            .WithResolvedSkulabsItem()
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return ReconcileResult.Empty;
        }

        // Loaded separately rather than Include'd, because WithResolvedSkulabsItem projects and a
        // projection discards includes — the navigation would silently arrive null and every
        // variant would look like it had never been reconciled. Loading into the same context lets
        // relationship fixup attach these to the tracked variants.
        var variantIds = candidates.Select(candidate => candidate.Variant.ShopifyProductVariantId).ToArray();
        await dbContext.DesiredItemStates
            .Where(state => variantIds.Contains(state.ShopifyProductVariantId))
            .LoadAsync(cancellationToken);

        // Shared across the whole pass so two variants merged together cannot be handed the same
        // generated SKU — neither is committed yet, so neither is visible to the other's check.
        var reservedSkus = new HashSet<string>(StringComparer.Ordinal);
        var variantsMarked = 0;
        var itemsMarked = 0;

        // SKUs this pass decided, so the collision check below can look at exactly those rather
        // than re-scanning the catalogue.
        var decidedSkus = new List<(Guid VariantId, string Sku)>();

        foreach (var candidate in candidates)
        {
            var isFirstDecision = candidate.Variant.DesiredState is null;
            var desired = await ResolveDesiredState(candidate.Variant, cancellationToken);
            var result = await mergeRules.Apply(
                BuildContext(candidate, desired, origin, reservedSkus),
                cancellationToken);

            ApplyDecisions(candidate.Variant, desired, result, isFirstDecision);

            if (result.Changed(ItemField.Sku) && desired.Sku.Length > 0)
            {
                decidedSkus.Add((candidate.Variant.ShopifyProductVariantId, desired.Sku));
            }

            if (MarkPendingShopify(candidate.Variant, desired))
            {
                variantsMarked++;
            }

            if (MarkPendingSkulabs(candidate.SkulabsItem, desired))
            {
                // Logged on the transition into pending rather than when the decision changed,
                // because the two are not the same moment: an item can fall out of step with a
                // title that was decided long ago — by being linked, or by being edited in SkuLabs.
                // Logging on the transition catches every such case exactly once, where logging on
                // the decision would miss them and logging unconditionally would repeat on every
                // pass until the push landed.
                if (!string.Equals(candidate.SkulabsItem!.Title, desired.Title, StringComparison.Ordinal))
                {
                    AddVariantLog(candidate.Variant,
                        VariantLogMessages.SkulabsTitleSyncedFromVariant(
                            candidate.SkulabsItem.Title, desired.Title));
                }

                itemsMarked++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await WarnOnSkuCollisions(decidedSkus, cancellationToken);

        if (variantsMarked > 0 || itemsMarked > 0)
        {
            logger.LogInformation(
                "Reconcile pass over {Candidates} variant(s) left {VariantsMarked} pending a Shopify push "
                + "and {ItemsMarked} pending a SkuLabs push.",
                candidates.Count, variantsMarked, itemsMarked);
        }

        return new ReconcileResult(variantsMarked, itemsMarked);
    }

    /// <summary>
    /// Reports a decided SKU that some other variant is also going to claim.
    /// <para>
    /// Deliberately a warning rather than a rejection. The usual way this arises is a SkuLabs code
    /// being adopted, and SkuLabs codes are accepted verbatim because they may already be printed on
    /// a label — refusing one here would only make our records disagree with the warehouse. But two
    /// variants sharing a SKU is what the create path's force-generation exists to prevent, so a
    /// collision arriving by another route should not pass silently.
    /// </para>
    /// </summary>
    private async Task WarnOnSkuCollisions(
        List<(Guid VariantId, string Sku)> decidedSkus,
        CancellationToken cancellationToken)
    {
        if (decidedSkus.Count == 0)
        {
            return;
        }

        var skus = decidedSkus.Select(decided => decided.Sku).ToArray();
        var holders = await dbContext.DesiredItemStates
            .Where(state => skus.Contains(state.Sku)
                            && !state.ShopifyProductVariant!.IsDeleted)
            .Select(state => new { state.Sku, state.ShopifyProductVariantId })
            .ToListAsync(cancellationToken);

        foreach (var (variantId, sku) in decidedSkus)
        {
            var others = holders
                .Where(holder => holder.Sku == sku && holder.ShopifyProductVariantId != variantId)
                .Select(holder => holder.ShopifyProductVariantId)
                .ToArray();

            if (others.Length == 0)
            {
                continue;
            }

            logger.LogWarning(
                "Variant {VariantId} now expects SKU '{Sku}', which {OtherCount} other variant(s) "
                + "also expect ({OtherVariantIds}). Accepted rather than rejected — a SkuLabs code may "
                + "already be on a label — but the duplicate needs resolving at source.",
                variantId, sku, others.Length, string.Join(", ", others));
        }
    }

    private MergeContext BuildContext(
        VariantWithSkulabsItem candidate,
        DesiredItemStateEntity desired,
        MergeOrigin origin,
        ISet<string> reservedSkus)
    {
        var variant = candidate.Variant;
        var item = candidate.SkulabsItem;

        var shopify = new ItemObservation(
            ObservedValue.Of(variant.Sku),
            ObservedValue.Of(variant.Barcode),
            ObservedValue.Of(variant.DisplayName),
            ObservedValue.Unobserved);

        // A variant with no usable link contributes nothing rather than a set of blanks — the
        // difference between "SkuLabs has no SKU for this" and "there is no SkuLabs item at all".
        var skulabs = item is null
            ? ItemObservation.None
            : new ItemObservation(
                ObservedValue.Of(item.Sku),
                ObservedValue.Of(item.Barcode),
                ObservedValue.Of(item.Title),
                ObservedValue.Of(item.Location));

        return new MergeContext(
            origin,
            variant.VariantId,
            variant.ProductTitle,
            variant.VariantTitle,
            shopify,
            skulabs,
            new MergeResult(desired.Sku, desired.Barcode, desired.Title, desired.Location),
            reservedSkus);
    }

    /// <summary>
    /// Fetches the variant's desired state, creating it on first sight. New variants arrive without
    /// one because ingest no longer decides anything — this pass is where a variant first acquires
    /// an opinion about what it should hold.
    /// </summary>
    private async Task<DesiredItemStateEntity> ResolveDesiredState(
        ShopifyProductVariantEntity variant,
        CancellationToken cancellationToken)
    {
        if (variant.DesiredState is { } existing)
        {
            return existing;
        }

        // A concurrent writer may have created it between our read and now; the unique index would
        // otherwise turn that race into a failed save for the whole batch.
        var stored = await dbContext.DesiredItemStates
            .FirstOrDefaultAsync(
                state => state.ShopifyProductVariantId == variant.ShopifyProductVariantId,
                cancellationToken);

        if (stored is not null)
        {
            variant.DesiredState = stored;
            return stored;
        }

        var created = new DesiredItemStateEntity
        {
            ShopifyProductVariantId = variant.ShopifyProductVariantId
        };

        dbContext.DesiredItemStates.Add(created);
        variant.DesiredState = created;
        return created;
    }

    /// <summary>
    /// Writes the merge outcome onto the desired state, one audit event per field that actually
    /// moved. Every change is a decision a merchant may later need explained, which is why the
    /// events are written at the point of decision rather than reconstructed at push time.
    /// </summary>
    /// <param name="isFirstDecision">
    /// Whether this variant is acquiring a desired state for the first time. Seeding an initial
    /// decision is not a change to one, so most fields go unlogged — otherwise every variant would
    /// open its history with four events reading "changed from nothing". The SKU and barcode are
    /// the exceptions: what identity a variant was assigned is exactly what a merchant comes to the
    /// history to find out.
    /// </param>
    private void ApplyDecisions(
        ShopifyProductVariantEntity variant,
        DesiredItemStateEntity desired,
        MergeResult result,
        bool isFirstDecision)
    {
        if (!result.HasChanges)
        {
            return;
        }

        if (result.Changed(ItemField.Sku))
        {
            if (result.Sku.Length > 0)
            {
                AddVariantLog(variant, desired.Sku.Length == 0
                    ? VariantLogMessages.SkuSet(result.Sku)
                    : VariantLogMessages.SkuUpdated(desired.Sku, result.Sku));
            }

            desired.Sku = result.Sku;
        }

        if (result.Changed(ItemField.Barcode))
        {
            if (result.Barcode.Length > 0)
            {
                AddVariantLog(variant, desired.Barcode.Length == 0
                    ? VariantLogMessages.BarcodeSet(result.Barcode)
                    : VariantLogMessages.BarcodeUpdated(desired.Barcode, result.Barcode));
            }

            desired.Barcode = result.Barcode;
        }

        // No event for a title change on its own. Ingest already logged the display name moving,
        // and what a merchant actually wants recorded — that the linked SkuLabs item is now out of
        // step and will be corrected — is logged where that becomes true, on the transition into
        // pending. Logging here as well would double up whenever the two coincide.
        if (result.Changed(ItemField.Title))
        {
            desired.Title = result.Title;
        }

        if (result.Changed(ItemField.Location))
        {
            if (!isFirstDecision)
            {
                AddVariantLog(variant, VariantLogMessages.LocationUpdated(desired.Location, result.Location));
            }

            desired.Location = result.Location;
        }

        desired.UpdatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// A variant owes Shopify a push exactly when the codes it should hold differ from the ones
    /// Shopify last reported. Recomputed rather than accumulated, so a mirror catching up — whether
    /// by a push or by a webhook — clears the flag without anyone having to remember to.
    /// </summary>
    private static bool MarkPendingShopify(ShopifyProductVariantEntity variant, DesiredItemStateEntity desired)
    {
        var pending = !string.Equals(desired.Sku, variant.Sku, StringComparison.Ordinal)
                      || !string.Equals(desired.Barcode, variant.Barcode, StringComparison.Ordinal);

        var newlyMarked = pending && !variant.PendingShopifySync;
        variant.PendingShopifySync = pending;

        return newlyMarked;
    }

    /// <summary>
    /// The SkuLabs side of the same comparison, over the only two fields we ever push there. Codes
    /// are excluded by construction rather than by policy — see the SKU and barcode merge rules.
    /// </summary>
    private static bool MarkPendingSkulabs(SkulabsItemEntity? item, DesiredItemStateEntity desired)
    {
        if (item is null)
        {
            return false;
        }

        var pending = !string.Equals(desired.Title, item.Title, StringComparison.Ordinal)
                      || !string.Equals(desired.Location, item.Location, StringComparison.Ordinal);

        var newlyMarked = pending && !item.PendingSkulabsSync;
        item.PendingSkulabsSync = pending;

        return newlyMarked;
    }

    private void AddVariantLog(ShopifyProductVariantEntity variant, string message)
    {
        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = variant.ShopifyProductVariantId,
            Message = message
        });
    }
}
