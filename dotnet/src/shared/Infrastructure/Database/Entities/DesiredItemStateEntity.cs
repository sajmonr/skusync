namespace Infrastructure.Database.Entities;

/// <summary>
/// What a variant and its linked SkuLabs item <em>should</em> hold, as opposed to what either
/// system currently does. One row per Shopify variant.
/// <para>
/// This is the half of the model that <see cref="ShopifyProductVariantEntity"/> and
/// <see cref="SkulabsItemEntity"/> deliberately no longer carry. Those two are pure mirrors —
/// ingest overwrites them with whatever the external system last said, without consulting anything.
/// The reconciler is the only writer here, and it is the only place that decides which side of a
/// disagreement wins.
/// </para>
/// <para>
/// Separating the two is what makes ingest safe to run unconditionally. While the variant row was
/// both mirror and desired state, refreshing it from a payload risked destroying a local correction
/// that had not been pushed yet, which is why ingest used to skip fields, refuse incoming values,
/// and only refresh metadata when a link moved. None of that is needed once a correction lives
/// somewhere ingest never writes.
/// </para>
/// </summary>
public class DesiredItemStateEntity
{
    public Guid DesiredItemStateId { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// The variant this state belongs to. Keyed on the variant rather than on the SkuLabs link
    /// because state has to outlive the link: a generated SKU exists before any SkuLabs item does,
    /// and a link that turns ambiguous must not take an un-pushed correction down with it.
    /// </summary>
    public Guid ShopifyProductVariantId { get; set; }

    /// <summary>The SKU both systems should end up holding. Pushed to Shopify; never pushed to SkuLabs.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>The barcode both systems should end up holding. Pushed to Shopify; never pushed to SkuLabs.</summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// The title the linked SkuLabs item should hold. Composed from the Shopify product and variant
    /// titles, and pushed to SkuLabs — Shopify is authoritative, so it is never pushed back there.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The bin location the linked SkuLabs item should hold in the configured warehouse.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedOnUtc { get; set; } = DateTime.UtcNow;

    public ShopifyProductVariantEntity? ShopifyProductVariant { get; set; }
}
