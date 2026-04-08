using System.Text.Json.Serialization;

namespace Integration.Skulabs.Items;

/// <summary>
/// Represents a single channel listing within a SkuLabs item response, linking the
/// SkuLabs item to a specific Shopify product variant.
/// </summary>
public class SkulabsListingResponse
{
    /// <summary>Gets or sets the Shopify variant ID associated with this listing.</summary>
    [JsonPropertyName("variant_id")]
    public string VariantId { get; set; } = "";

    /// <summary>Gets or sets the Shopify product ID associated with this listing.</summary>
    [JsonPropertyName("item_id")]
    public string ProductId { get; set; } = "";

    /// <summary>Gets or sets the internal SkuLabs listing identifier (<c>_id</c> field).</summary>
    [JsonPropertyName("_id")]
    public string ListingId { get; set; } = "";
}
