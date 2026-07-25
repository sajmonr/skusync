using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Application.Products.Webhook;

/// <summary>
/// Handles the <c>products/delete</c> Shopify webhook topic. When a product is deleted in
/// Shopify, this handler terminally marks every locally-tracked variant of that product as
/// deleted so the change is reflected in real time rather than waiting for the next full sync.
/// The <c>products/delete</c> payload is minimal — essentially just the product id — so removal
/// is driven off <see cref="SqsShopEventProduct.Id"/>, never a variant list.
/// </summary>
public class ShopifyProductDeleteWebhookHandler(
    ApplicationDbContext dbContext,
    ILogger<ShopifyProductDeleteWebhookHandler> logger,
    IFeatureManager featureManager)
    : IShopifyWebhookHandler
{
    /// <inheritdoc/>
    public string TopicName => ShopifyWebhookTopic.ProductsDelete;

    /// <summary>
    /// Marks all stored variants of the deleted product as terminally deleted. Already-deleted
    /// rows are skipped so redelivery is idempotent. <see cref="ShopifyProductVariantEntity.IsActive"/>
    /// is left untouched — deletion and deactivation are independent lifecycle flags.
    /// </summary>
    /// <param name="product">The product payload from the <c>products/delete</c> webhook.</param>
    public async Task Handle(SqsShopEventProduct product)
    {
        if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifySyncEnabled))
        {
            logger.LogDebug(
                "{Flag} is disabled. Ignoring products/delete webhook for product {ProductId}.",
                FeatureFlags.ShopifySyncEnabled, product.Id);
            return;
        }

        var variants = await dbContext.ShopifyProductVariants
            .Where(variant => variant.ProductId == product.Id)
            .ToArrayAsync();

        logger.LogDebug(
            "Loaded {Count} variants for deleted product {ProductId}.",
            variants.Length, product.Id);

        var deletedOn = DateTime.UtcNow;
        var markedCount = 0;

        foreach (var entity in variants)
        {
            if (entity.IsDeleted)
            {
                continue;
            }

            entity.IsDeleted = true;
            entity.DeletedOn = deletedOn;
            entity.UpdatedOnUtc = deletedOn;

            dbContext.ShopifyProductVariantLogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                ShopifyProductVariantId = entity.ShopifyProductVariantId,
                Message = VariantLogMessages.DeletedFromShopify()
            });

            logger.LogInformation(
                "Marking variant {VariantId} (GlobalVariantId {GlobalVariantId}) of product {ProductId} as deleted; the product was deleted in Shopify.",
                entity.VariantId, entity.GlobalVariantId, product.Id);

            markedCount++;
        }

        if (markedCount == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Marked {Count} variant(s) of product {ProductId} as deleted following a products/delete webhook.",
            markedCount, product.Id);
    }
}
