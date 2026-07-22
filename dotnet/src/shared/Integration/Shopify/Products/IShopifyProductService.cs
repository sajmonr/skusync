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

    /// <summary>
    /// Updates the specified Shopify product variants with the provided details.
    /// </summary>
    /// <param name="productId">The unique identifier of the Shopify product to update.</param>
    /// <param name="variants">A collection of <see cref="ShopifyUpdateProductVariant"/> containing the updated variant details.</param>
    /// <returns>A boolean value indicating whether the update operation was successful.</returns>
    Task<bool> UpdateVariants(string productId, IEnumerable<ShopifyUpdateProductVariant> variants);
}