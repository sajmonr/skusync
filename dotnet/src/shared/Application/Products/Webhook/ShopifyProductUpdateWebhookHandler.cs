using Application.Products.Services;
using Application.Sync;
using Application.Sync.Merge;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/update</c> Shopify webhook topic: mirrors the incoming variant data
/// locally — creating rows for variants not yet tracked, refreshing those we hold, marking absent
/// ones deleted — then reconciles the touched variants and dispatches whatever became pending.
/// <para>
/// Mirroring is unconditional and includes SKU and barcode, which this handler used to refuse to
/// copy. It could not, while the variant row was also the authoritative value; now that decided
/// values live in the desired state, recording what Shopify actually holds is what tells the
/// reconciler whether Shopify is owed anything.
/// </para>
/// </summary>
public class ShopifyProductUpdateWebhookHandler(
    ApplicationDbContext dbContext,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IReconciler reconciler,
    IShopifyDispatchTrigger dispatchTrigger,
    IFeatureManager featureManager)
    : ShopifyWebhookBase, IShopifyWebhookHandler
{
    /// <inheritdoc/>
    public string TopicName => ShopifyWebhookTopic.ProductsUpdate;

    /// <summary>
    /// Reconciles the incoming product payload with local database state, then synchronises
    /// any changed variants back to Shopify.
    /// </summary>
    /// <param name="product">The product payload from the <c>products/update</c> webhook.</param>
    public async Task Handle(SqsShopEventProduct product)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifySyncEnabled))
        {
            logger.LogDebug(
                "{Flag} is disabled. Ignoring products/update webhook for product {ProductId}.",
                FeatureFlags.ShopifySyncEnabled, product.Id);
            return;
        }

        // Deactivated rows (IsActive=false after repeated failed Shopify pushes) must be matched
        // here too — otherwise the lookup below misses them and we insert a fresh row, violating
        // the unique GlobalVariantId/VariantId index on every redelivery. There is no global
        // query filter, so a plain query already sees them.
        var existingVariants = await dbContext.ShopifyProductVariants
            .Where(variant => variant.ProductId == product.Id)
            .ToArrayAsync();

        logger.LogDebug(
            "Loaded {Count} variants for product {ProductId}. We currently have {ExistingCount} variants.",
            product.Variants.Count, product.Id, existingVariants.Length);

        // Collect events before SaveChangesAsync so we only publish for persisted changes.
        var createdEntities = new List<ShopifyProductVariantEntity>();
        // Every variant the payload named that we already track, whether or not mirroring changed
        // anything. A payload that matches the mirror exactly still names variants whose decisions
        // may be stale — one marked pending by an earlier pass, say — and skipping those would let
        // a webhook that "changed nothing" leave a push unmade until the next sweep.
        var matchedEntities = new List<ShopifyProductVariantEntity>();

        // update entities
        foreach (var variant in product.Variants)
        {
            var entity = existingVariants.FirstOrDefault(e => e.VariantId == variant.Id);

            if (entity is null)
            {
                logger.LogInformation(
                    "Newly-seen variant {VariantId} of product {ProductId} on a products/update webhook.",
                    variant.Id, product.Id);

                var newEntity = ConstructEntity(product, variant);
                newEntity.LogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    Message = VariantLogMessages.VariantCreated()
                });

                dbContext.ShopifyProductVariants.Add(newEntity);
                createdEntities.Add(newEntity);
            }
            else
            {
                // A variant marked deleted is gone from Shopify for good; a matching id here can
                // only be a stale redelivery. Deletion is terminal, so leave the row frozen —
                // never resurrect or mutate it. A genuinely returning variant arrives under a new
                // id and is created fresh above.
                if (entity.IsDeleted)
                {
                    logger.LogWarning(
                        "products/update for product {ProductId} referenced variant {VariantId}, which is marked deleted. Ignoring — a returning variant is tracked as a new row.",
                        product.Id, variant.Id);
                    continue;
                }

                // A products/update for a variant we'd previously deactivated means it's live in
                // Shopify again — revive it so it re-enters the drift sweep.
                ReactivateIfDormant(entity);
                UpdateEntity(entity, product, variant);
                matchedEntities.Add(entity);
            }
        }

        MarkVariantsRemovedFromShopify(product, existingVariants);

        var droppedInserts = await dbContext.SaveChangesToleratingVariantConflicts(logger);

        // Reconcile and dispatch only after a successful save, skipping any newly-seen variant a
        // concurrent writer had already committed under us — the writer that won the race handles
        // its own row. Reconcile runs first so a title change reaches the linked SkuLabs item; the
        // immediate dispatch then pushes whatever became pending toward Shopify within seconds.
        //
        // The two groups reconcile separately because their origins differ, and the origin is what
        // the SKU and barcode rules key off: a first sighting has its payload codes replaced, an
        // existing variant keeps what was already decided for it.
        var createdVariantIds = createdEntities
            .Where(e => !droppedInserts.Contains(e))
            .Select(e => e.ShopifyProductVariantId)
            .ToArray();
        var matchedVariantIds = matchedEntities
            .Select(e => e.ShopifyProductVariantId)
            .ToArray();

        await reconciler.ReconcileVariants(createdVariantIds, MergeOrigin.WebhookCreate);
        await reconciler.ReconcileVariants(matchedVariantIds, MergeOrigin.Routine);

        await dispatchTrigger.TryDispatch(
            await PendingAmong([.. createdVariantIds, .. matchedVariantIds]));
    }

    /// <summary>
    /// Narrows a set of touched variants to those the reconcile actually left owing Shopify a push.
    /// Mirroring a payload touches rows without necessarily changing what they should hold, so
    /// handing the dispatcher everything would report a push for webhooks that decided nothing.
    /// </summary>
    private async Task<Guid[]> PendingAmong(IReadOnlyCollection<Guid> variantIds) =>
        variantIds.Count == 0
            ? []
            : await dbContext.ShopifyProductVariants
                .Where(variant => variantIds.Contains(variant.ShopifyProductVariantId)
                                  && variant.PendingShopifySync)
                .Select(variant => variant.ShopifyProductVariantId)
                .ToArrayAsync();

    /// <summary>
    /// Marks any locally-tracked variant of this product that is absent from the incoming
    /// payload as deleted. A <c>products/update</c> payload carries the product's full current
    /// variant set, so a stored variant missing from it has been removed in Shopify — the classic
    /// case being a standalone default variant that Shopify drops when real variants are created.
    /// The row is kept (never physically deleted) so its history survives; the flag is terminal.
    /// </summary>
    private void MarkVariantsRemovedFromShopify(
        SqsShopEventProduct product,
        IReadOnlyList<ShopifyProductVariantEntity> existingVariants)
    {
        // An empty variant list is never a legitimate product state in Shopify; treat it as a
        // malformed payload and skip removal detection rather than deleting every stored variant.
        if (product.Variants.Count == 0)
        {
            return;
        }

        var payloadVariantIds = product.Variants.Select(variant => variant.Id).ToHashSet();

        foreach (var entity in existingVariants)
        {
            if (entity.IsDeleted || payloadVariantIds.Contains(entity.VariantId))
            {
                continue;
            }

            entity.IsDeleted = true;
            entity.DeletedOn = DateTime.UtcNow;
            entity.UpdatedOnUtc = DateTime.UtcNow;

            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = entity.ShopifyProductVariantId,
                Message = VariantLogMessages.DeletedFromShopify()
            });

            logger.LogInformation(
                "Marking variant {VariantId} (GlobalVariantId {GlobalVariantId}) of product {ProductId} as deleted; it is absent from the products/update payload.",
                entity.VariantId, entity.GlobalVariantId, product.Id);
        }
    }

    private void ReactivateIfDormant(ShopifyProductVariantEntity entity)
    {
        if (entity.IsActive)
        {
            return;
        }

        entity.IsActive = true;
        entity.FailedShopifySyncAttempts = 0;

        dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
        {
            ShopifyProductVariantId = entity.ShopifyProductVariantId,
            Message = VariantLogMessages.Reactivated()
        });

        logger.LogInformation(
            "Reactivating previously-deactivated variant {VariantId} after a products/update webhook.",
            entity.VariantId);
    }

    /// <summary>
    /// Refreshes the mirror from the payload — a straight copy, including SKU and barcode.
    /// <para>
    /// Copying the codes is the change that makes this a mirror. It previously refused to, because
    /// the row doubled as the authoritative value and overwriting it would have destroyed a
    /// correction that had not been pushed yet; instead it flagged the divergence and left the row
    /// alone. With the decided values living in the desired state, recording what Shopify actually
    /// holds is both safe and necessary — it is the other half of the comparison that determines
    /// what Shopify is owed.
    /// </para>
    /// </summary>
    private bool UpdateEntity(ShopifyProductVariantEntity entity, SqsShopEventProduct product,
        SqsShopEventVariant variant)
    {
        var changed = false;

        var newDisplayName = ShopifyDisplayName.Compose(product.Title, variant.Title);
        if (entity.DisplayName != newDisplayName)
        {
            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = entity.ShopifyProductVariantId,
                Message = VariantLogMessages.TitleUpdated(entity.DisplayName, newDisplayName)
            });
            logger.LogDebug("Updating display name for variant {VariantId}: [{OldName}] -> [{NewName}].",
                variant.Id, entity.DisplayName, newDisplayName);
            entity.DisplayName = newDisplayName;
            changed = true;
        }

        var incomingSku = variant.Sku ?? "";
        if (!string.Equals(entity.Sku, incomingSku, StringComparison.Ordinal))
        {
            logger.LogDebug("Shopify reports SKU '{Sku}' for variant {VariantId}; mirroring it.",
                incomingSku, variant.Id);
            entity.Sku = incomingSku;
            changed = true;
        }

        var incomingBarcode = variant.Barcode ?? "";
        if (!string.Equals(entity.Barcode, incomingBarcode, StringComparison.Ordinal))
        {
            logger.LogDebug("Shopify reports barcode '{Barcode}' for variant {VariantId}; mirroring it.",
                incomingBarcode, variant.Id);
            entity.Barcode = incomingBarcode;
            changed = true;
        }

        if (changed)
        {
            entity.UpdatedOnUtc = DateTime.UtcNow;
        }

        return changed;
    }
}
