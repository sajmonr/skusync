using System.Net.Http.Headers;
using System.Net.Http.Json;
using Integration.Skulabs.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Skulabs.Items;

/// <summary>
/// HTTP client for the SkuLabs Items API. Retrieves inventory items along with their
/// Shopify listing associations. Base URL and API key are configured from
/// <see cref="SkulabsApiOptions"/>.
/// </summary>
public class SkulabsItemClient
{
    private readonly HttpClient _client;
    private readonly ILogger<SkulabsItemClient> _logger;
    
    public SkulabsItemClient(HttpClient httpClient, IOptionsMonitor<SkulabsApiOptions> optionsMonitor, ILogger<SkulabsItemClient> logger)
    {
        _logger = logger;
        _client = httpClient;
        
        _client.BaseAddress = new Uri(optionsMonitor.CurrentValue.BaseUrl);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", optionsMonitor.CurrentValue.ApiKey);
    }
    
    /// <summary>
    /// Fetches all SkuLabs inventory items that have at least one Shopify channel listing.
    /// Only the <c>name</c>, <c>sku</c>, <c>upc</c>, and <c>listings</c> fields are
    /// requested from the API to minimise payload size.
    /// </summary>
    /// <returns>An array of <see cref="SkuLabsItem"/> records with Shopify identifiers populated.</returns>
    public async Task<SkuLabsItem[]> GetAllItems()
    {
        const string fields = """
                              {"_id": 1, "name": 1, "sku": 1, "upc": 1, "listings": 1}
                              """;
        var queryParams = new Dictionary<string, string>
            { { "fields", fields } };
        var queryString = string.Join("&", queryParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        var response = await _client.GetAsync($"item/get?{queryString}");
        
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadFromJsonAsync<SkulabsItemResponse[]>();
        var finalItems = content?
            .Where(item => item.Listings.Length > 0)
            .Select(SkuLabsItem.FromResponse).ToArray() ?? [];

        return finalItems;
    }

    public Task<bool> UpdateItem()
    {
        return Task.FromResult(true);
    }
    
}