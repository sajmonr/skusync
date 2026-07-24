namespace Integration.Shopify.Products;

/// <summary>
/// Defines a service for interacting with Shopify products and their variants.
/// </summary>
public interface IShopifyProductService
{
    /// <summary>
    /// Retrieves every Shopify product variant in the store, paginating the full variant set.
    /// An empty array is authoritative — it means the store genuinely has no variants. Failures
    /// are propagated as exceptions (never swallowed into an empty result) so callers can tell a
    /// failed fetch apart from an empty store; this distinction matters because the full sync uses
    /// absence from this set to mark local variants as deleted.
    /// </summary>
    /// <returns>An array of <see cref="ShopifyProductVariant"/> representing every variant in the Shopify store.</returns>
    /// <exception cref="Exception">Propagated from the underlying Shopify GraphQL call when the fetch fails.</exception>
    Task<ShopifyProductVariant[]> GetProducts();

    /// <summary>
    /// Updates the specified Shopify product variants with the provided details.
    /// </summary>
    /// <param name="productId">The unique identifier of the Shopify product to update.</param>
    /// <param name="variants">A collection of <see cref="ShopifyUpdateProductVariant"/> containing the updated variant details.</param>
    /// <returns>A boolean value indicating whether the update operation was successful.</returns>
    Task<bool> UpdateVariants(string productId, IEnumerable<ShopifyUpdateProductVariant> variants);
}