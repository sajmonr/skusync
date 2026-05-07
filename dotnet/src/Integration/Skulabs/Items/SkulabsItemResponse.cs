using System.Text.Json.Serialization;

namespace Integration.Skulabs.Items;

/// <summary>
/// Represents the raw JSON response shape for a single item returned by the SkuLabs API.
/// Use <see cref="SkuLabsItem.FromResponse"/> to convert to the application domain model.
/// </summary>
public class SkulabsItemResponse
{
    /// <summary>Gets or sets the internal SkuLabs item identifier (<c>_id</c> field).</summary>
    [JsonPropertyName("_id")]
    public string ItemId { get; init; } = "";

    /// <summary>
    /// Gets or sets the collection of channel listings associated with this item.
    /// Each listing links the SkuLabs item to a specific Shopify product variant.
    /// </summary>
    [JsonPropertyName("listings")]
    public SkulabsListingResponse[] Listings { get; init; } = [];

    /// <summary>Gets or sets the stock-keeping unit (SKU) assigned to this item.</summary>
    [JsonPropertyName("sku")]
    public string Sku { get; init; } = "";

    /// <summary>Gets or sets the UPC/EAN barcode assigned to this item.</summary>
    [JsonPropertyName("upc")]
    public string Upc { get; init; } = "";

    [JsonPropertyName("name")]
    public string Title { get; init; } = "";
}
