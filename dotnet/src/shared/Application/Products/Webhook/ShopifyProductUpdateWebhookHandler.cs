using Application.Products.Events;
using Application.Products.Services;
using Application.Skus;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using SlimMessageBus;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/update</c> Shopify webhook topic. Reconciles incoming variant
/// data with the local database — creating new variant records for any variants not yet
/// tracked and updating titles for existing ones — then pushes any SKU or barcode
/// discrepancies back to Shopify.
/// </summary>
public class ShopifyProductUpdateWebhookHandler(
    ApplicationDbContext dbContext,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IMessageBus messageBus,
    IFeatureManager featureManager,
    ISkuGenerator skuGenerator)
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
        var updatedEntities = new List<ShopifyProductVariantEntity>();
        // Track SKUs assigned within this batch so two new variants of the same product
        // can't be issued the same generated SKU before they hit the database.
        var reservedSkus = new HashSet<string>(StringComparer.Ordinal);

        // update entities
        foreach (var variant in product.Variants)
        {
            var entity = existingVariants.FirstOrDefault(e => e.VariantId == variant.Id);

            if (entity is null)
            {
                var generatedSku = await skuGenerator.Generate(
                    product.Title, variant.Title, reservedSkus,
                    fallbackSegment: variant.Id.ToString());
                reservedSkus.Add(generatedSku);

                logger.LogInformation(
                    "Assigning generated SKU '{Sku}' to newly-seen variant {VariantId} of product {ProductId}.",
                    generatedSku, variant.Id, product.Id);

                var newEntity = ConstructEntity(product, variant, generatedSku);

                newEntity.LogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    Message = VariantLogMessages.VariantCreated()
                });
                newEntity.LogEvents.Add(new ShopifyProductVariantLogEventEntity
                {
                    Message = VariantLogMessages.SkuSet(generatedSku)
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

                // Update it
                var didChange = UpdateEntity(entity, product, variant);
                var didBarcodeOrSkuChange = DidBarcodeOrSkuChange(entity, variant);

                if (!didChange && !didBarcodeOrSkuChange)
                {
                    continue;
                }

                updatedEntities.Add(entity);
            }
        }

        MarkVariantsRemovedFromShopify(product, existingVariants);

        var droppedInserts = await dbContext.SaveChangesToleratingVariantConflicts(logger);

        // Enqueue only after a successful save so no phantom events enter the queue, and skip any
        // newly-seen variant a concurrent writer had already committed under us.
        await messageBus.PublishBatch(
            updatedEntities.Select(e => new ProductVariantUpdatedEvent(e.ShopifyProductVariantId)));
        await messageBus.PublishBatch(createdEntities
            .Where(e => !droppedInserts.Contains(e))
            .Select(e => new ProductVariantCreatedEvent(e.ShopifyProductVariantId)));
    }

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

        return changed;
    }

    private bool DidBarcodeOrSkuChange(ShopifyProductVariantEntity entity, SqsShopEventVariant variant)
    {
        if (!string.IsNullOrEmpty(entity.Barcode) && entity.Barcode != variant.Barcode)
        {
            logger.LogDebug("Barcode for variant {VariantId} does not match in Shopify. Updating it.",
                variant.Id);

            return true;
        }

        if (!string.IsNullOrEmpty(entity.Sku) && entity.Sku != variant.Sku)
        {
            logger.LogDebug("SKU for variant {VariantId} does not match in Shopify. Updating it.",
                variant.Id);

            return true;
        }

        return false;
    }
}
