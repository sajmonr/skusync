using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Integration.RateLimiting;
using Integration.Skulabs.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Skulabs.Items;

/// <summary>
/// HTTP client for the SkuLabs Items API. Retrieves inventory items along with their
/// Shopify listing associations. Base URL and API key are configured from
/// <see cref="SkulabsApiOptions"/>.
/// </summary>
public class SkulabsItemClient : ISkulabsItemClient
{
    private readonly HttpClient _client;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<SkulabsItemClient> _logger;

    public SkulabsItemClient(
        HttpClient httpClient,
        IOptionsMonitor<SkulabsApiOptions> optionsMonitor,
        IRateLimitService rateLimitService,
        ILogger<SkulabsItemClient> logger
    )
    {
        _logger = logger;
        _client = httpClient;
        _rateLimitService = rateLimitService;

        _client.BaseAddress = new Uri(optionsMonitor.CurrentValue.BaseUrl);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            optionsMonitor.CurrentValue.ApiKey
        );
    }

    private void ThrowIfRateLimited(string requestPath)
    {
        if (_rateLimitService.GetRemainingCooldown(SkulabsRateLimitHandler.RateLimitKey) is not { } remaining)
        {
            return;
        }

        _logger.LogWarning(
            "Skipping SkuLabs request to {RequestPath}; client is in rate-limit cooldown for {RemainingSeconds}s.",
            requestPath,
            remaining.TotalSeconds);
        throw new RateLimitedException(SkulabsRateLimitHandler.RateLimitKey, remaining);
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
        ThrowIfRateLimited(requestPath);
        _logger.LogDebug("Requesting all items from SkuLabs at {RequestPath}.", requestPath);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.GetAsync(requestPath);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            await LogErrorResponse(response, requestPath, stopwatch.ElapsedMilliseconds);
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

        var content = await response.Content.ReadFromJsonAsync<SkulabsItemResponse[]>();

        if (content is null)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"SkuLabs item response deserialized to null. Body: {Truncate(body)}");
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

    /// <summary>
    /// Updates one or more SkuLabs items in a single call via <c>PUT /item/bulk_upsert</c>.
    /// </summary>
    public async Task UpdateItems(IEnumerable<SkulabsItemUpdateWithId> updates)
    {
        const string requestPath = "item/bulk_upsert";
        ThrowIfRateLimited(requestPath);
        var items = updates
            .Select(u => new BulkUpsertItem(u.Id, u.Name))
            .ToArray();
        var payload = new BulkUpsertPayload(items);

        _logger.LogDebug(
            "Bulk-updating {Count} SkuLabs item(s) at {RequestPath}.",
            items.Length,
            requestPath);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.PutAsJsonAsync(requestPath, payload);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            await LogErrorResponse(response, requestPath, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogInformation(
                "SkuLabs bulk-upsert of {Count} item(s) completed with status {StatusCode} in {ElapsedMs}ms.",
                items.Length,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }

        response.EnsureSuccessStatusCode();
    }

    private readonly record struct BulkUpsertPayload(
        [property: JsonPropertyName("items")] BulkUpsertItem[] Items);

    private readonly record struct BulkUpsertItem(
        [property: JsonPropertyName("_id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private async Task LogErrorResponse(HttpResponseMessage response, string requestPath, long elapsedMs)
    {
        var body = await response.Content.ReadAsStringAsync();
        SkulabsErrorPayload? error = null;
        try
        {
            error = JsonSerializer.Deserialize<SkulabsErrorResponse>(body)?.Error;
        }
        catch (JsonException)
        {
            // Body wasn't the standardized envelope (e.g. a fronting proxy returned HTML on 502).
            // Fall through and log the raw body instead.
        }

        if (error is not null)
        {
            _logger.LogError(
                "SkuLabs items request to {RequestPath} failed with status {StatusCode} after {ElapsedMs}ms. "
                + "Code: {ErrorCode}, Message: {ErrorMessage}, Overview: {Overview}, Origin: {Origin}, "
                + "TraceId: {SkulabsTraceId}, UserError: {UserError}.",
                requestPath,
                (int)response.StatusCode,
                elapsedMs,
                error.Code,
                error.Message,
                error.Overview,
                error.Origin,
                error.SkulabsTraceId,
                error.UserError);
        }
        else
        {
            _logger.LogError(
                "SkuLabs items request to {RequestPath} failed with status {StatusCode} after {ElapsedMs}ms. "
                + "Body: {ResponseBody}",
                requestPath,
                (int)response.StatusCode,
                elapsedMs,
                Truncate(body));
        }
    }

    private static string Truncate(string value, int max = 2048) =>
        value.Length <= max ? value : value[..max] + "…";

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
