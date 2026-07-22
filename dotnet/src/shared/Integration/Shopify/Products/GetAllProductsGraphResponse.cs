namespace Integration.Shopify.Products;

internal record GetAllProductsGraphResponse
{
    public required ShopifySharp.GraphQL.ProductConnection Products { get; init; }
}