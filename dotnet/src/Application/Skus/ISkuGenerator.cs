namespace Application.Skus;

/// <summary>
/// Produces deterministic, human-readable SKUs from a product title and variant title,
/// guaranteeing uniqueness across the local database (and within the in-progress batch
/// supplied via <paramref name="reservedInBatch"/>) by appending a numeric suffix when
/// the base SKU would otherwise collide.
/// </summary>
public interface ISkuGenerator
{
    /// <summary>
    /// Generates a SKU for a new variant. The returned value is guaranteed not to exist
    /// in <c>ShopifyProductVariants.Sku</c> at the moment of generation and not to appear
    /// in <paramref name="reservedInBatch"/>.
    /// </summary>
    /// <param name="productTitle">The product's title (used for the product abbreviation).</param>
    /// <param name="variantTitle">
    /// The variant's title, as Shopify exposes it (slash-delimited, e.g. "Large / Black").
    /// Pass <c>null</c>, empty, or the literal sentinel "Default Title" for products with
    /// no options — the variant segment is then omitted from the SKU.
    /// </param>
    /// <param name="reservedInBatch">
    /// Optional set of SKUs already chosen for variants in the current save batch but not
    /// yet persisted. Callers should add the returned SKU to this set before generating
    /// the next variant so that two new variants of the same product never receive the
    /// same SKU.
    /// </param>
    Task<string> Generate(
        string productTitle,
        string? variantTitle,
        ISet<string>? reservedInBatch = null,
        CancellationToken cancellationToken = default);
}
