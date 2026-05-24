using System.Net.Http.Headers;
using System.Net.Http.Json;
using Integration.Skulabs.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Skulabs.Items;

/// <summary>
/// Abstraction over the SkuLabs Items API client to enable substitution in tests.
/// </summary>
public interface ISkulabsItemClient
{
    /// <summary>
    /// Fetches all SkuLabs inventory items that have exactly one Shopify channel listing.
    /// </summary>
    Task<SkuLabsItem[]> GetAllItems();
}

/// <summary>
/// HTTP client for the SkuLabs Items API. Retrieves inventory items along with their
/// Shopify listing associations. Base URL and API key are configured from
/// <see cref="SkulabsApiOptions"/>.
/// </summary>
public class SkulabsItemClient : ISkulabsItemClient
{
    private readonly HttpClient _client;
    private readonly ILogger<SkulabsItemClient> _logger;

    public SkulabsItemClient(
        HttpClient httpClient,
        IOptionsMonitor<SkulabsApiOptions> optionsMonitor,
        ILogger<SkulabsItemClient> logger
    )
    {
        _logger = logger;
        _client = httpClient;

        _client.BaseAddress = new Uri(optionsMonitor.CurrentValue.BaseUrl);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            optionsMonitor.CurrentValue.ApiKey
        );
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
        var queryParams = new Dictionary<string, string> { { "fields", fields } };
        var queryString = string.Join(
            "&",
            queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"
            )
        );
        var requestPath = $"item/get?{queryString}";
        _logger.LogDebug("Requesting all items from SkuLabs at {RequestPath}.", requestPath);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.GetAsync(requestPath);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            // The resilience handler has already exhausted retries on transient failures, so
            // anything that reaches here is either a permanent 4xx or a sustained 5xx. Log the
            // status before EnsureSuccessStatusCode throws so the exception in the upstream
            // catch has context (HttpRequestException's message alone hides which call failed).
            _logger.LogError(
                "SkuLabs items request to {RequestPath} failed with status {StatusCode} after {ElapsedMs}ms.",
                requestPath,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds
            );
        }
        else
        {
            _logger.LogDebug(
                "SkuLabs items request completed with status {StatusCode} in {ElapsedMs}ms.",
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds
            );
        }

        response.EnsureSuccessStatusCode();

        try
        {
            var content = await response.Content.ReadFromJsonAsync<SkulabsItemResponse[]>();

            if (content is null)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Response from SkuLabs API was empty. Response body: {ResponseBody}",
                    body
                );
                return [];
            }

            var singleListingItems = FilterMultipleListings(content);
            var finalItems = FilterNonNumericVariantIds(singleListingItems);

            _logger.LogInformation(
                "SkuLabs returned {RawCount} item(s); {Usable} usable, {Filtered} filtered out.",
                content.Length,
                finalItems.Length,
                content.Length - finalItems.Length
            );

            return finalItems;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to parse SkuLabs item response.");
            return [];
        }
    }

    private SkulabsItemResponse[] FilterMultipleListings(SkulabsItemResponse[] responses)
    {
        var singleListing = responses.Where(r => r.Listings.Length == 1).ToArray();
        var multipleListings = responses.Length - singleListing.Length;

        if (multipleListings > 0)
        {
            _logger.LogWarning(
                "{Count} SkuLabs item(s) had multiple listings and were filtered out.",
                multipleListings);
        }

        return singleListing;
    }

    private SkuLabsItem[] FilterNonNumericVariantIds(SkulabsItemResponse[] responses)
    {
        var items = new List<SkuLabsItem>(responses.Length);
        var nonNumeric = 0;

        foreach (var response in responses)
        {
            var listing = response.Listings[0];
            if (!long.TryParse(listing.VariantId, out var variantId))
            {
                nonNumeric++;
                continue;
            }

            items.Add(new SkuLabsItem(
                response.ItemId,
                listing.ListingId,
                variantId,
                response.Sku,
                response.Upc,
                response.Title));
        }

        if (nonNumeric > 0)
        {
            _logger.LogWarning(
                "{Count} SkuLabs item(s) had a non-numeric Shopify variant ID and were filtered out.",
                nonNumeric);
        }

        return items.ToArray();
    }
}
