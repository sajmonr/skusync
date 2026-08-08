using Application.Products.Services;
using Application.Sync;
using Application.Sync.Merge;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/create</c> Shopify webhook topic: mirrors each variant of the new
/// product locally, then hands off to the reconciler, which decides the codes and marks whatever it
/// changed as owed to Shopify. An immediate dispatch follows so a generated SKU reaches Shopify in
/// seconds rather than on the next cadence.
/// <para>
/// The speed matters because SkuLabs runs its own Shopify sync on a schedule we neither see nor
/// control. Whichever code is in Shopify when that sync runs is the one SkuLabs adopts and freezes,
/// so this is a race to be first — though not one correctness depends on winning.
/// </para>
/// </summary>
public class ShopifyProductCreateWebhookHandler(
    ApplicationDbContext dbContext,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IReconciler reconciler,
    IShopifyDispatchTrigger dispatchTrigger,
    IFeatureManager featureManager)
    : ShopifyWebhookBase, IShopifyWebhookHandler
{

    /// <inheritdoc/>
    public string TopicName => ShopifyWebhookTopic.ProductsCreate;

    /// <summary>
    /// Mirrors all variants of the newly created product, reconciles them, and dispatches whatever
    /// became pending.
    /// </summary>
    /// <param name="product">The product payload from the <c>products/create</c> webhook.</param>
    public async Task Handle(SqsShopEventProduct product)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifySyncEnabled))
        {
            logger.LogDebug(
                "{Flag} is disabled. Ignoring products/create webhook for product {ProductId}.",
                FeatureFlags.ShopifySyncEnabled, product.Id);
            return;
        }

        // Shopify can redeliver products/create webhooks (e.g. retries, replays) and the second
        // delivery may carry a different variant set. Skip variants we already track so we don't
        // violate the unique GlobalVariantId index, but still persist any genuinely new ones.
        var existingVariantIds = await dbContext.ShopifyProductVariants
            .Where(v => v.ProductId == product.Id)
            .Select(v => v.VariantId)
            .ToHashSetAsync();

        var entities = new List<ShopifyProductVariantEntity>();

        foreach (var variant in product.Variants)
        {
            if (existingVariantIds.Contains(variant.Id))
            {
                logger.LogInformation(
                    "Skipping variant {VariantId} for product {ProductId} — already tracked locally.",
                    variant.Id, product.Id);
                continue;
            }

            logger.LogInformation(
                "New variant {VariantId} [{VariantTitle}] for product {ProductId} found.",
                variant.Id, variant.Title, product.Id);

            var newEntity = ConstructEntity(product, variant);
            newEntity.LogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                Message = VariantLogMessages.VariantCreated()
            });

            entities.Add(newEntity);
        }

        if (entities.Count == 0)
        {
            return;
        }

        await dbContext.ShopifyProductVariants.AddRangeAsync(entities);
        var droppedInserts = await dbContext.SaveChangesToleratingVariantConflicts(logger);

        // Reconcile and dispatch only after a successful save, skipping any variant a concurrent
        // writer had already committed — the writer that won the race handles its own row.
        var trackedVariantIds = entities
            .Where(e => !droppedInserts.Contains(e))
            .Select(e => e.ShopifyProductVariantId)
            .ToArray();

        // WebhookCreate, not Routine: a variant first seen this way is most often a duplicated
        // product whose codes were never cleared, so the merge rules replace them rather than
        // adopt them. The import deliberately makes the opposite choice.
        await reconciler.ReconcileVariants(trackedVariantIds, MergeOrigin.WebhookCreate);

        // Only what the reconcile actually left owing Shopify a push — a redelivery that decided
        // nothing should not look like a dispatch.
        var pendingVariantIds = await dbContext.ShopifyProductVariants
            .Where(variant => trackedVariantIds.Contains(variant.ShopifyProductVariantId)
                              && variant.PendingShopifySync)
            .Select(variant => variant.ShopifyProductVariantId)
            .ToArrayAsync();

        await dispatchTrigger.TryDispatch(pendingVariantIds);
    }
}
