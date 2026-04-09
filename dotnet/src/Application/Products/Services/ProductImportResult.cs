namespace Application.Products.Services;

/// <summary>
/// Represents the result of a product import operation in a Shopify integration.
/// </summary>
public readonly record struct ProductImportResult(bool IsSuccess, int Created, int Updated, string Error)
{
    /// <summary>
    /// Creates a successful result for a product import operation, indicating the number of products created and updated.
    /// </summary>
    /// <param name="created">The number of products successfully created.</param>
    /// <param name="updated">The number of products successfully updated.</param>
    /// <return>A <see cref="ProductImportResult"/> instance representing a successful operation.</return>
    public static ProductImportResult Success(int created, int updated) => new(true, created, updated, "");

    /// <summary>
    /// Creates a failed result for a product import operation, including an error message describing the failure.
    /// </summary>
    /// <param name="error">The error message detailing the reason for the failure.</param>
    /// <return>A <see cref="ProductImportResult"/> instance representing a failed operation.</return>
    public static ProductImportResult Failure(string error) => new(false, 0, 0, error);
    
}