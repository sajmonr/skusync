using Integration.Shopify.GraphQl;
using Microsoft.Extensions.Logging;
using ShopifySharp.GraphQL;

namespace Integration.Shopify.Products;

internal class ShopifyProductService(IShopifyGraphQlService graphQlService, ILogger<ShopifyProductService> logger)
    : IShopifyProductService
{
    public async Task<ShopifyProductVariant[]> GetProducts()
    {
        try
        {
            var allVariants = new List<ShopifyProductVariant>();
            var page = 1;
            var filter = CreateFilter();

            logger.LogDebug("Starting Shopify product fetch.");

            while (true)
            {
                var response =
                    await graphQlService.ExecuteAsync<GetAllProductsGraphResponse>(GetAllProductsQuery, filter);
                var newItems = response.Products.nodes.SelectMany(product => ToProductVariants(product!))
                    .ToArray();

                allVariants.AddRange(newItems);

                logger.LogDebug("Fetched Shopify page {Page}. Products: {ProductCount}, variants: {VariantCount}.",
                    page, response.Products.nodes.Count(), newItems.Length);

                if (!response.Products.pageInfo!.hasNextPage)
                {
                    break;
                }

                filter = CreateFilter(response.Products.pageInfo.endCursor);
                page++;
            }

            logger.LogDebug("Completed Shopify product fetch. Total variants: {VariantCount}.", allVariants.Count);
            return allVariants.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch products from Shopify.");
            throw;
        }
    }

    private static Dictionary<string, object?> CreateFilter(string? endCursor = null)
    {
        return new Dictionary<string, object?> { { "after", endCursor } };
    }

    private static IEnumerable<ShopifyProductVariant> ToProductVariants(Product product)
    {
        return product!.variants!.nodes.Select(variant =>
            new ShopifyProductVariant(
                product.id ?? string.Empty,
                variant!.id ?? string.Empty,
                GetVariantTitle(variant),
                variant.sku ?? string.Empty,
                variant.barcode ?? string.Empty));

        string GetVariantTitle(ProductVariant variant)
        {
            if (string.IsNullOrWhiteSpace(variant.title) || variant.title == "Default Title")
            {
                return product.title ?? string.Empty;
            }

            return variant.title;
        }
    }

    private const string GetAllProductsQuery = """
                                               query GetProducts($after: String){
                                                   products(first: 250, after: $after){
                                                       nodes{
                                                           id
                                                           title
                                                           variants(first: 50){
                                                               nodes{
                                                                   id
                                                                   title
                                                                   barcode
                                                                   sku
                                                               }
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