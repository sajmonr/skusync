namespace Integration.Shopify.Products;

/// <summary>
/// Defines a service for interacting with Shopify products and their variants.
/// </summary>
public interface IShopifyProductService
{
    /// <summary>
    /// Retrieves a collection of Shopify product variants.
    /// </summary>
    /// <returns>An array of <see cref="ShopifyProductVariant"/> representing the product variants available in the Shopify store.</returns>
    Task<ShopifyProductVariant[]> GetProducts();
}