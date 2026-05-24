using Application.Products.Events;
using Application.Products.Services;
using Application.Skus;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using SlimMessageBus;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/create</c> Shopify webhook topic. When a new product is created
/// in Shopify, this handler persists each of its variants to the local database and then
/// writes the generated SKU and barcode back to Shopify.
/// </summary>
public class ShopifyProductCreateWebhookHandler(
    ApplicationDbContext dbContext,
    ILogger<ShopifyProductUpdateWebhookHandler> logger,
    IMessageBus messageBus,
    IFeatureManager featureManager,
    ISkuGenerator skuGenerator)
    : ShopifyWebhookBase, IShopifyWebhookHandler
{

    /// <inheritdoc/>
    public string TopicName => "products/create";

    /// <summary>
    /// Persists all variants of the newly created product and pushes the generated SKU
    /// and barcode values back to Shopify via the Admin API.
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
        // Track SKUs assigned within this batch so two new variants of the same product
        // can't be issued the same generated SKU before they hit the database.
        var reservedSkus = new HashSet<string>(StringComparer.Ordinal);

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

            var generatedSku = await skuGenerator.GenerateAsync(
                product.Title, variant.Title, reservedSkus);
            reservedSkus.Add(generatedSku);

            logger.LogInformation(
                "Assigning generated SKU '{Sku}' to variant {VariantId} of product {ProductId}.",
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

            entities.Add(newEntity);
        }

        if (entities.Count == 0)
        {
            return;
        }

        await dbContext.ShopifyProductVariants.AddRangeAsync(entities);
        await dbContext.SaveChangesAsync();

        // Enqueue only after a successful save so no phantom events enter the queue.
        await messageBus.PublishBatch(
            entities.Select(e => new ProductVariantCreatedEvent(e.ShopifyProductVariantId)));
    }
}