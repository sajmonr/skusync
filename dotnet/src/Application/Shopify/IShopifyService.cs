namespace Application.Shopify;

/// <summary>
/// Defines the contract for synchronizing product data between Shopify and the local database.
/// </summary>
public interface IShopifyService
{
    /// <summary>
    /// Imports products from Shopify into the local database. This operation ensures synchronization
    /// between the Shopify store and the application's local data store.
    /// </summary>
    /// <returns>
    /// A <see cref="ProductImportResult"/> structure containing the outcome of the import operation.
    /// This includes whether the operation was successful, the number of products created and updated,
    /// or an error message in the case of failure.
    /// </returns>
    Task<ProductImportResult> ImportProducts();
}
