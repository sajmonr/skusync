namespace Integration.Shopify.Products;

internal record GetAllProductVariantsGraphResponse
{
    public required ShopifySharp.GraphQL.ProductVariantConnection ProductVariants { get; init; }
}
