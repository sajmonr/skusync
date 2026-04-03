using System.Net.Http.Headers;
using System.Net.Http.Json;
using Integration.Skulabs.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Skulabs.Items;

public class ItemClient
{
    private readonly HttpClient _client;
    private readonly ILogger<ItemClient> _logger;
    
    public ItemClient(HttpClient httpClient, IOptionsMonitor<SkulabsApiOptions> optionsMonitor, ILogger<ItemClient> logger)
    {
        _logger = logger;
        _client = httpClient;
        
        _client.BaseAddress = new Uri(optionsMonitor.CurrentValue.BaseUrl);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", optionsMonitor.CurrentValue.ApiKey);
    }
    
    public async Task<SkuLabsItem[]> GetAllItems()
    {
        const string fields = """
                              {"name": 1, "sku": 1, "upc": 1, "listings": 1}
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
}