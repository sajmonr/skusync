namespace Infrastructure.Database.Entities;

/// <summary>
/// A Shopify variant paired with the SkuLabs item it unambiguously resolves to, or <c>null</c> when it
/// resolves to none. Produced by <see cref="SkulabsItemLinks.WithResolvedSkulabsItem"/> so that read
/// paths can filter, sort and project against a plain nullable reference instead of restating the
/// cardinality rules at every call site.
/// </summary>
/// <remarks>
/// A class rather than a record struct because it carries tracked entity references and exists purely
/// as an EF projection target.
/// </remarks>
public sealed class VariantWithSkulabsItem
{
    public ShopifyProductVariantEntity Variant { get; init; } = null!;

    public SkulabsItemEntity? SkulabsItem { get; init; }
}
