using System.Text.Json.Serialization;

namespace Integration.Skulabs.Items;

public class SkulabsItemResponse
{
    [JsonPropertyName("_id")] public string Id { get; set; } = "";

    [JsonPropertyName("listings")]
    public SkulabsListingResponse[] Listings { get; set; } = [];

    [JsonPropertyName("sku")] public string Sku { get; set; } = "";

    [JsonPropertyName("upc")]
    public string Upc { get; set; } = "";
}