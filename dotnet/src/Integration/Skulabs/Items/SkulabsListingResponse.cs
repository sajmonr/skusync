using System.Text.Json.Serialization;

namespace Integration.Skulabs.Items;

public class SkulabsListingResponse
{
    [JsonPropertyName("variant_id")] public string VariantId { get; set; } = "";

    [JsonPropertyName("item_id")]
    public string ProductId { get; set; } = "";

    [JsonPropertyName("_id")]
    public string ListingId { get; set; } = "";
}