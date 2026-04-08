namespace Application.Shopify;

/// <summary>
/// Defines the contract for synchronizing product data between Shopify and the local database.
/// </summary>
public interface IShopifyImportService
{
    /// <summary>
    /// Fetches all product variants from Shopify and upserts them into the local database.
    /// New variants are inserted; existing variants have their title, SKU, and barcode updated
    /// where applicable.
    /// </summary>
    Task ImportProducts();
}
