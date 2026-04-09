using Application.Jobs;
using Application.Products.Services;
using Infrastructure.Database;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Quartz;

namespace Application.Products.Jobs;

[DisallowConcurrentExecution]
[MutexGroup("shopify-sync")]
public class ProductDeduplicationJob(
    IProductsService productsService,
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext,
    ILogger<ProductDeduplicationJob> logger,
    IFeatureManager featureManager) : IJob
{
    public static readonly JobKey Key = new(nameof(ProductDeduplicationJob), "product");

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("ProductDeduplicationJob started.");
        logger.LogDebug(
            "Triggered by '{TriggerKey}'. Scheduled fire time: {ScheduledFireTime}. Actual fire time: {FireTime}.",
            context.Trigger.Key,
            context.ScheduledFireTimeUtc?.ToString("o") ?? "N/A",
            context.FireTimeUtc.ToString("o"));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var deduplicationResult = await productsService.DeduplicateProducts();

            if (!deduplicationResult.IsSuccess)
            {
                stopwatch.Stop();
                logger.LogError("Product deduplication failed with error: {ErrorMessage}", deduplicationResult.Error);
                return;
            }

            if (deduplicationResult.VariantIds.Length == 0)
            {
                stopwatch.Stop();
                logger.LogInformation(
                    "ProductDeduplicationJob completed. No duplicates found. Elapsed: {ElapsedMs}ms.",
                    stopwatch.ElapsedMilliseconds);
                return;
            }

            logger.LogInformation(
                "Deduplication resolved {Count} variant(s). Pushing updated SKUs and barcodes to Shopify.",
                deduplicationResult.VariantIds.Length);

            var affectedVariants = await dbContext.ShopifyProductVariants
                .Where(v => deduplicationResult.VariantIds.Contains(v.VariantId))
                .ToListAsync();

            logger.LogDebug("Fetched {Count} affected variant entity(s) from the database.", affectedVariants.Count);

            if (!await featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack))
            {
                logger.LogInformation(
                    "ShopifyWriteBack feature flag is disabled. Skipping Shopify update for {Count} variant(s).",
                    affectedVariants.Count);
                stopwatch.Stop();
                return;
            }

            var variantsByProduct = affectedVariants.GroupBy(v => v.GlobalProductId);

            foreach (var productGroup in variantsByProduct)
            {
                var productId = productGroup.Key;
                var variantsToUpdate = productGroup
                    .Select(v => new ShopifyUpdateProductVariant(v.GlobalVariantId, v.Sku, v.Barcode))
                    .ToArray();

                logger.LogDebug(
                    "Updating {Count} variant(s) in Shopify for product {ProductId}.",
                    variantsToUpdate.Length,
                    productId);

                var success = await shopifyProductService.UpdateVariants(productId, variantsToUpdate);

                if (!success)
                {
                    logger.LogError(
                        "Failed to update {Count} variant(s) in Shopify for product {ProductId}.",
                        variantsToUpdate.Length,
                        productId);
                }
                else
                {
                    logger.LogDebug(
                        "Successfully updated {Count} variant(s) in Shopify for product {ProductId}.",
                        variantsToUpdate.Length,
                        productId);
                }
            }

            stopwatch.Stop();
            logger.LogInformation(
                "ProductDeduplicationJob completed successfully in {ElapsedMs}ms. Next scheduled fire time: {NextFireTime}.",
                stopwatch.ElapsedMilliseconds,
                context.NextFireTimeUtc?.ToString("o") ?? "none");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "ProductDeduplicationJob failed after {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
