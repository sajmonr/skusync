using Application.Jobs.Maintenance;
using Application.Products.Services;
using Microsoft.Extensions.Logging;

namespace Application.Products.Maintenance;

/// <summary>
/// Maintenance task that triggers a full Shopify product import and, on success, follows up
/// with a deduplication pass. Invoked by <see cref="ProductMaintenanceJob"/>; not registered
/// as a standalone Quartz job.
/// </summary>
public class ShopifyProductSyncTask(
    IProductsService productsService,
    ILogger<ShopifyProductSyncTask> logger
) : IMaintenanceTask
{
    public string Name => nameof(ShopifyProductSyncTask);

    public async Task Execute(CancellationToken cancellationToken)
    {
        var importResult = await productsService.ImportProductsFromShopify();

        if (!importResult.IsSuccess)
        {
            logger.LogError(
                "Shopify product import failed with error: {ErrorMessage}",
                importResult.Error
            );
            return;
        }

        await productsService.DeduplicateProducts();
    }
}
