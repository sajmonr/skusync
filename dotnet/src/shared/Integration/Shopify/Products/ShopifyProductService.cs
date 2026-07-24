using Integration.Shopify.GraphQl;
using Microsoft.Extensions.Logging;
using ShopifySharp.GraphQL;

namespace Integration.Shopify.Products;

internal class ShopifyProductService(IShopifyGraphQlService graphQlService, ILogger<ShopifyProductService> logger)
    : IShopifyProductService
{
    public async Task<ShopifyProductVariant[]> GetProducts()
    {
        // Paginates the shop-wide productVariants connection so every variant is returned — the
        // previous per-product variants(first: 50) sub-selection silently truncated products with
        // more than 50 variants. Exceptions are intentionally NOT swallowed: the caller treats a
        // thrown error as a failed fetch (and skips removal reconciliation), whereas an empty
        // result is an authoritative "the shop has no variants". Returning [] on error would blur
        // those two cases and could drive the full sync to delete the entire catalogue.
        var allVariants = new List<ShopifyProductVariant>();
        var page = 1;
        var filter = CreateFilter();

        logger.LogDebug("Starting Shopify product variant fetch.");

        while (true)
        {
            var response =
                await graphQlService.ExecuteAsync<GetAllProductVariantsGraphResponse>(GetAllProductVariantsQuery, filter);

            var newItems = response.ProductVariants.nodes
                .Where(variant => variant is not null)
                .Select(variant => ToProductVariant(variant!))
                .ToArray();

            allVariants.AddRange(newItems);

            logger.LogDebug("Fetched Shopify variant page {Page}. Variants: {VariantCount}.",
                page, newItems.Length);

            if (!response.ProductVariants.pageInfo!.hasNextPage)
            {
                break;
            }

            filter = CreateFilter(response.ProductVariants.pageInfo.endCursor);
            page++;
        }

        logger.LogDebug("Completed Shopify product variant fetch. Total variants: {VariantCount}.", allVariants.Count);
        return allVariants.ToArray();
    }

    public async Task<bool> UpdateVariants(string productId, IEnumerable<ShopifyUpdateProductVariant> variants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        
        var shopifyProductVariants = variants as ShopifyUpdateProductVariant[] ?? variants.ToArray();

        if (shopifyProductVariants.Length == 0)
        {
            logger.LogDebug("No variants to update for product ID [{ProductId}].", productId);
            return true;
        }
        
        try
        {
            logger.LogDebug("Updating variants for product ID [{ProductId}] in Shopify.", productId);
            
            var variables = new Dictionary<string, object?>
            {
                { "productId", productId },
                { "variants", shopifyProductVariants.Select(variant => new
                {
                    id = variant.GlobalVariantId,
                    barcode = variant.Barcode,
                    inventoryItem = new { sku = variant.Sku }
                }) }
            };
            
            var response = await graphQlService.ExecuteAsync<UpdateVariantsGraphResponse>(BulkUpdateVariantsQuery, variables);

            if (response.UserErrors is null)
            {
                logger.LogDebug("Successfully updated variants for product ID [{ProductId}] in Shopify.", productId);
                return true;
            }
            
            logger.LogError("Failed to update variants for product ID [{ProductId}] in Shopify. Errors: {Errors}", productId, response.UserErrors);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to update variants for product ID [{ProductId}] in Shopify.", productId);
            return false;
        }
    }

    private static Dictionary<string, object?> CreateFilter(string? endCursor = null)
    {
        return new Dictionary<string, object?> { { "after", endCursor } };
    }

    private static ShopifyProductVariant ToProductVariant(ProductVariant variant)
    {
        var product = variant.product;
        var productTitle = product?.title ?? string.Empty;
        var variantTitle = variant.title ?? string.Empty;
        return new ShopifyProductVariant(
            product?.id ?? string.Empty,
            variant.id ?? string.Empty,
            ShopifyDisplayName.Compose(productTitle, variantTitle),
            variant.sku ?? string.Empty,
            variant.barcode ?? string.Empty)
        {
            ProductTitle = productTitle,
            VariantTitle = variantTitle,
        };
    }

    private const string BulkUpdateVariantsQuery = """
                                                   mutation ProductVariantsBulkUpdate($productId: ID!, $variants: [ProductVariantsBulkInput!]!) {
                                                    productVariantsBulkUpdate(productId: $productId, variants: $variants){
                                                        userErrors {
                                                            field
                                                            message
                                                        }
                                                    }
                                                   }
                                                   """;

    private const string GetAllProductVariantsQuery = """
                                                      query GetProductVariants($after: String){
                                                          productVariants(first: 250, after: $after){
                                                              nodes{
                                                                  id
                                                                  title
                                                                  barcode
                                                                  sku
                                                                  product{
                                                                      id
                                                                      title
                                                                  }
                                                              }
                                                              pageInfo{
                                                                  hasNextPage
                                                                  endCursor
                                                              }
                                                          }
                                                      }
                                                      """;
}