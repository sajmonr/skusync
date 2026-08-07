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
    private readonly string _warehouseId;

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
        // Normalised here rather than at each use: WarehouseId is not [Required], so nothing
        // validates it and a configured null would otherwise reach the alias_locations lookup.
        _warehouseId = optionsMonitor.CurrentValue.WarehouseId ?? "";

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
    /// Fetches every SkuLabs inventory item with all of its channel listings intact. Only the
    /// <c>name</c>, <c>sku</c>, <c>upc</c>, <c>listings</c> and — when a warehouse is configured —
    /// <c>alias_locations</c> fields are requested from the API to minimise payload size. Deciding
    /// which items are syncable is left to the caller.
    /// </summary>
    /// <returns>A <see cref="SkulabsItemCollection"/> wrapping every item SkuLabs returned.</returns>
    public async Task<SkulabsItemCollection> GetAllItems()
    {
        var fields = _warehouseId.Length == 0
            ? """
              {"_id": 1, "name": 1, "sku": 1, "upc": 1, "listings": 1}
              """
            : """
              {"_id": 1, "name": 1, "sku": 1, "upc": 1, "listings": 1, "alias_locations": 1}
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

        var items = content.Select(response => MapItem(response, _warehouseId)).ToArray();

        _logger.LogInformation("SkuLabs returned {RawCount} item(s).", items.Length);

        return new SkulabsItemCollection(items);
    }

    private static SkulabsApiItem MapItem(SkulabsItemResponse response, string warehouseId)
    {
        // SkuLabs sends explicit `null` for a listing's variant_id / item_id on non-Shopify listings,
        // which System.Text.Json writes over the "" property defaults. Coalesce back to "" to keep the
        // non-null string invariant SkulabsApiListing assumes; SkulabsItemCollection then drops any
        // listing whose variant id is not a Shopify (numeric) id.
        var listings = response.Listings
            .Select(listing => new SkulabsApiListing(
                listing.ListingId ?? "",
                listing.VariantId ?? "",
                listing.ProductId ?? ""))
            .ToArray();

        // With no warehouse configured we never asked for alias_locations, so we know nothing rather
        // than "no location" — reporting "" would tell the caller to erase what it already holds.
        // Otherwise both "no alias_locations at all" and "located in some other warehouse" collapse
        // to "", the same absent-means-empty treatment the listing ids above get.
        var location = warehouseId.Length == 0
            ? null
            : response.AliasLocations.GetValueOrDefault(warehouseId) ?? "";

        return new SkulabsApiItem(
            response.ItemId,
            response.Title,
            response.Sku,
            response.Upc,
            location,
            listings);
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
}
