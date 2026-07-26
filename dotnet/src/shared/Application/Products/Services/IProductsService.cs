namespace Application.Products.Services;

/// <summary>
/// Defines the contract for synchronizing product data between Shopify and the local database.
/// </summary>
public interface IProductsService
{
    /// <summary>
    /// Runs a full product sync — imports from Shopify, then deduplicates — as a single unit. This is
    /// the entry point enqueued as a background job (the manual "sync now" trigger and the scheduled
    /// maintenance run). Throws when either phase fails so the background-job runner records the run
    /// as failed rather than silently succeeding.
    /// </summary>
    /// <param name="cancellationToken">Supplied by the job runner; cancels a run in progress.</param>
    Task SyncProducts(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports products from Shopify into the local database. This operation ensures synchronization
    /// between the Shopify store and the application's local data store.
    /// </summary>
    /// <returns>
    /// A <see cref="ProductImportResult"/> structure containing the outcome of the import operation.
    /// This includes whether the operation was successful, the number of products created and updated,
    /// or an error message in the case of failure.
    /// </returns>
    Task<ProductImportResult> ImportProductsFromShopify();

    /// <summary>
    /// Scans the local database for product variants that share a non-unique SKU or barcode with at least one
    /// other variant and overwrites the duplicated fields with the variant's own numeric Shopify variant ID.
    /// </summary>
    /// <returns>
    /// A <see cref="ProductDeduplicationResult"/> containing whether the operation succeeded, the numeric variant
    /// IDs that were modified, or an error message on failure.
    /// </returns>
    Task<ProductDeduplicationResult> DeduplicateProducts();
}
