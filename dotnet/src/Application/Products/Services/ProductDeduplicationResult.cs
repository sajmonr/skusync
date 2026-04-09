namespace Application.Products.Services;

/// <summary>
/// Represents the result of a product deduplication operation.
/// </summary>
public readonly record struct ProductDeduplicationResult(bool IsSuccess, long[] VariantIds, string Error)
{
    /// <summary>
    /// Creates a successful result, including the numeric variant IDs that were deduplicated.
    /// </summary>
    /// <param name="variantIds">The numeric Shopify variant IDs whose SKU and barcode were overwritten.</param>
    /// <returns>A <see cref="ProductDeduplicationResult"/> instance representing a successful operation.</returns>
    public static ProductDeduplicationResult Success(long[] variantIds) => new(true, variantIds, "");

    /// <summary>
    /// Creates a failed result with an error message describing the failure.
    /// </summary>
    /// <param name="error">The error message detailing the reason for the failure.</param>
    /// <returns>A <see cref="ProductDeduplicationResult"/> instance representing a failed operation.</returns>
    public static ProductDeduplicationResult Failure(string error) => new(false, [], error);
}
