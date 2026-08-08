using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Integration.RateLimiting;
using Integration.Skulabs.Items;
using Integration.Skulabs.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Tests.Integration.Skulabs.Items;

public class SkulabsItemClientTests
{
    private const string BaseUrl = "https://api.skulabs.test/";
    private const string ApiKey = "test-api-key";
    private const string WarehouseId = "69912a8923657b958806a418";
    private const string OtherWarehouseId = "79912a8923657b958806a419";

    private readonly IOptionsMonitor<SkulabsApiOptions> _options =
        Substitute.For<IOptionsMonitor<SkulabsApiOptions>>();

    private readonly TestLogger<SkulabsItemClient> _logger = new();
    private readonly RecordingHttpMessageHandler _handler = new();
    private readonly IRateLimitService _rateLimitService = Substitute.For<IRateLimitService>();

    public SkulabsItemClientTests()
    {
        UseWarehouse(WarehouseId);
        _rateLimitService.GetRemainingCooldown(Arg.Any<string>()).Returns((TimeSpan?)null);
    }

    private void UseWarehouse(string warehouseId) =>
        _options.CurrentValue.Returns(new SkulabsApiOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            WarehouseId = warehouseId
        });

    [Fact]
    public void Constructor_ShouldConfigureBaseAddressAndAuthorizationHeader()
    {
        var httpClient = new HttpClient(_handler);

        _ = new SkulabsItemClient(httpClient, _options, _rateLimitService, _logger);

        httpClient.BaseAddress.ShouldBe(new Uri(BaseUrl));
        httpClient.DefaultRequestHeaders.Authorization.ShouldBe(
            new AuthenticationHeaderValue("Bearer", ApiKey));
    }

    [Fact]
    public async Task GetAllItems_ShouldThrowAndNotSendRequest_WhenClientIsInRateLimitCooldown()
    {
        _rateLimitService.GetRemainingCooldown(SkulabsRateLimitHandler.RateLimitKey)
            .Returns(TimeSpan.FromMinutes(2));
        var sut = CreateSut();

        var exception = await Should.ThrowAsync<RateLimitedException>(() => sut.GetAllItems());

        exception.Key.ShouldBe(SkulabsRateLimitHandler.RateLimitKey);
        exception.RetryAfter.ShouldBe(TimeSpan.FromMinutes(2));
        _handler.Requests.ShouldBeEmpty();
        _logger.Entries.ShouldContain(e =>
            e.LogLevel == LogLevel.Warning && e.Message.Contains("cooldown"));
    }

    [Fact]
    public async Task UpdateItems_ShouldThrowAndNotSendRequest_WhenClientIsInRateLimitCooldown()
    {
        _rateLimitService.GetRemainingCooldown(SkulabsRateLimitHandler.RateLimitKey)
            .Returns(TimeSpan.FromSeconds(45));
        var sut = CreateSut();

        await Should.ThrowAsync<RateLimitedException>(() =>
            sut.UpdateItems([new SkulabsItemUpdateWithId("item-1", "Name")]));

        _handler.Requests.ShouldBeEmpty();
        _logger.Entries.ShouldContain(e =>
            e.LogLevel == LogLevel.Warning && e.Message.Contains("cooldown"));
    }

    [Fact]
    public async Task GetAllItems_ShouldSendGetRequestToItemGetEndpoint_WithFieldsQuery()
    {
        _handler.SetResponse(JsonResponse("[]"));
        var sut = CreateSut();

        await sut.GetAllItems();

        _handler.Requests.Count.ShouldBe(1);
        var request = _handler.Requests[0];
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsoluteUri.ShouldStartWith($"{BaseUrl}item/get?");
        request.RequestUri.Query.ShouldContain("fields=");
        Uri.UnescapeDataString(request.RequestUri.Query)
            .ShouldContain(
                "\"_id\": 1, \"name\": 1, \"sku\": 1, \"upc\": 1, \"listings\": 1, \"alias_locations\": 1");
    }

    [Fact]
    public async Task GetAllItems_ShouldNotRequestAliasLocations_WhenNoWarehouseIsConfigured()
    {
        // An unset warehouse id is the off-switch: nothing to resolve the map against, so don't
        // pay for it in the payload either.
        UseWarehouse("");
        _handler.SetResponse(JsonResponse("[]"));
        var sut = CreateSut();

        await sut.GetAllItems();

        Uri.UnescapeDataString(_handler.Requests[0].RequestUri!.Query)
            .ShouldNotContain("alias_locations");
    }

    [Fact]
    public async Task GetAllItems_ShouldReportUnknownLocation_WhenNoWarehouseIsConfigured()
    {
        // Null, not "": with the switch off we never asked, so we have no opinion to hand downstream.
        // Reporting "" would read as "this item has no location" and invite the caller to erase one.
        UseWarehouse("");
        _handler.SetResponse(JsonResponse("""
                                          [
                                            {
                                              "_id": "item-1",
                                              "name": "Item",
                                              "sku": "SKU",
                                              "upc": "UPC",
                                              "listings": [],
                                              "alias_locations": { "69912a8923657b958806a418": "A-01-06" }
                                            }
                                          ]
                                          """));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Items.ShouldHaveSingleItem().Location.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllItems_ShouldMapLocationVerbatim_WhenItDoesNotMatchTheHouseFormat()
    {
        // Bin labels are typed by humans, so the real data has stragglers that miss the usual
        // LETTER-NN-NN shape. SkuLabs is the source of truth: mirror what it says rather than
        // normalising or rejecting, which would misreport where the stock actually is.
        _handler.SetResponse(JsonResponse("""
                                          [
                                            {
                                              "_id": "item-1",
                                              "name": "Item",
                                              "sku": "SKU",
                                              "upc": "UPC",
                                              "listings": [],
                                              "alias_locations": { "69912a8923657b958806a418": "c-12-03" }
                                            }
                                          ]
                                          """));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Items.ShouldHaveSingleItem().Location.ShouldBe("c-12-03");
    }

    [Fact]
    public async Task GetAllItems_ShouldMapWarehouseLocation_ForEveryShapeOfAliasLocations()
    {
        const string json = """
                            [
                              {
                                "_id": "item-located",
                                "name": "Located",
                                "sku": "SKU-L",
                                "upc": "UPC-L",
                                "listings": [],
                                "alias_locations": { "69912a8923657b958806a418": "A-01-06" }
                              },
                              {
                                "_id": "item-elsewhere",
                                "name": "Elsewhere",
                                "sku": "SKU-E",
                                "upc": "UPC-E",
                                "listings": [],
                                "alias_locations": { "79912a8923657b958806a419": "Z-99-01" }
                              },
                              {
                                "_id": "item-no-map",
                                "name": "No Map",
                                "sku": "SKU-N",
                                "upc": "UPC-N",
                                "listings": []
                              },
                              {
                                "_id": "item-null-location",
                                "name": "Null Location",
                                "sku": "SKU-Z",
                                "upc": "UPC-Z",
                                "listings": [],
                                "alias_locations": { "69912a8923657b958806a418": null }
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Items.Single(item => item.SourceItemId == "item-located").Location.ShouldBe("A-01-06");

        // A map that names only other warehouses, no map at all, and an explicit null all mean the
        // same thing to us: this item has no location here.
        result.Items.Single(item => item.SourceItemId == "item-elsewhere").Location.ShouldBe("");
        result.Items.Single(item => item.SourceItemId == "item-no-map").Location.ShouldBe("");
        result.Items.Single(item => item.SourceItemId == "item-null-location").Location.ShouldBe("");
    }

    [Fact]
    public async Task GetAllItems_ShouldMapNoLocation_WhenWarehouseIsConfiguredButItemIsInAnotherOne()
    {
        UseWarehouse(OtherWarehouseId);
        _handler.SetResponse(JsonResponse("""
                                          [
                                            {
                                              "_id": "item-1",
                                              "name": "Item",
                                              "sku": "SKU",
                                              "upc": "UPC",
                                              "listings": [],
                                              "alias_locations": { "69912a8923657b958806a418": "A-01-06" }
                                            }
                                          ]
                                          """));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Items.ShouldHaveSingleItem().Location.ShouldBe("");
    }

    [Fact]
    public async Task GetAllItems_ShouldReturnEveryItem_WithOnlyItsShopifyListings()
    {
        const string json = """
                            [
                              {
                                "_id": "item-1",
                                "name": "Item One",
                                "sku": "SKU-1",
                                "upc": "UPC-1",
                                "listings": [
                                  { "variant_id": "1", "item_id": "prod-1", "_id": "listing-1" }
                                ]
                              },
                              {
                                "_id": "item-2",
                                "name": "Item Two",
                                "sku": "SKU-2",
                                "upc": "UPC-2",
                                "listings": []
                              },
                              {
                                "_id": "item-3",
                                "name": "Item Three",
                                "sku": "SKU-3",
                                "upc": "UPC-3",
                                "listings": [
                                  { "variant_id": "3", "item_id": "prod-3", "_id": "listing-3" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Items.Count.ShouldBe(3);

        var itemOne = result.Items.Single(item => item.SourceItemId == "item-1");
        itemOne.Name.ShouldBe("Item One");
        itemOne.Sku.ShouldBe("SKU-1");
        itemOne.Upc.ShouldBe("UPC-1");
        itemOne.Listings.Single().ShouldBe(new SkulabsApiListing("listing-1", "1", "prod-1"));

        result.Items.Single(item => item.SourceItemId == "item-3")
            .Listings.Single().ShouldBe(new SkulabsApiListing("listing-3", "3", "prod-3"));

        // An item with no Shopify listing is kept, with nothing to link it by.
        result.Items.Single(item => item.SourceItemId == "item-2").Listings.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllItems_ShouldThrow_WhenResponseBodyIsJsonNull()
    {
        _handler.SetResponse(JsonResponse("null"));
        var sut = CreateSut();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => sut.GetAllItems());
        exception.Message.ShouldContain("deserialized to null");
    }

    [Fact]
    public async Task GetAllItems_ShouldThrow_WhenResponseIsNotValidJson()
    {
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        });
        var sut = CreateSut();

        await Should.ThrowAsync<JsonException>(() => sut.GetAllItems());
    }

    [Fact]
    public async Task GetAllItems_ShouldLogStructuredErrorFields_WhenResponseIsStandardSkulabsErrorEnvelope()
    {
        const string errorBody = """
                                 {
                                   "error": {
                                     "message": "Invalid API key",
                                     "statusCode": 401,
                                     "code": "AUTH_INVALID",
                                     "overview": "Authentication failed",
                                     "origin": "auth-service",
                                     "skulabsTraceId": "trace-abc-123",
                                     "user_error": false
                                   }
                                 }
                                 """;
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
        });
        var sut = CreateSut();

        await Should.ThrowAsync<SkulabsRequestFailedException>(() => sut.GetAllItems());

        var errorEntry = _logger.Entries.SingleOrDefault(e => e.LogLevel == LogLevel.Error);
        errorEntry.ShouldNotBeNull();
        errorEntry.Message.ShouldContain("AUTH_INVALID");
        errorEntry.Message.ShouldContain("Invalid API key");
        errorEntry.Message.ShouldContain("trace-abc-123");
        errorEntry.Message.ShouldContain("auth-service");
        errorEntry.Message.ShouldContain("Authentication failed");
    }

    [Fact]
    public async Task GetAllItems_ShouldLogRawBody_WhenErrorResponseIsNotStandardEnvelope()
    {
        const string nonStandardBody = "<html><body>Bad Gateway</body></html>";
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(nonStandardBody, Encoding.UTF8, "text/html")
        });
        var sut = CreateSut();

        await Should.ThrowAsync<SkulabsRequestFailedException>(() => sut.GetAllItems());

        var errorEntry = _logger.Entries.SingleOrDefault(e => e.LogLevel == LogLevel.Error);
        errorEntry.ShouldNotBeNull();
        errorEntry.Message.ShouldContain("Bad Gateway");
    }

    [Fact]
    public async Task GetAllItems_ShouldPreserveEveryListing_WhenItemHasMultiple()
    {
        const string json = """
                            [
                              {
                                "_id": "item-multi-1",
                                "name": "Multi 1",
                                "sku": "SKU-M1",
                                "upc": "UPC-M1",
                                "listings": [
                                  { "variant_id": "10", "item_id": "prod-a", "_id": "l-a" },
                                  { "variant_id": "11", "item_id": "prod-b", "_id": "l-b" }
                                ]
                              },
                              {
                                "_id": "item-multi-2",
                                "name": "Multi 2",
                                "sku": "SKU-M2",
                                "upc": "UPC-M2",
                                "listings": [
                                  { "variant_id": "20", "item_id": "prod-c", "_id": "l-c" },
                                  { "variant_id": "21", "item_id": "prod-d", "_id": "l-d" }
                                ]
                              },
                              {
                                "_id": "item-single",
                                "name": "Single",
                                "sku": "SKU-S",
                                "upc": "UPC-S",
                                "listings": [
                                  { "variant_id": "30", "item_id": "prod-s", "_id": "l-s" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Items.Single(item => item.SourceItemId == "item-single")
            .Listings.Single().ShouldBe(new SkulabsApiListing("l-s", "30", "prod-s"));

        result.Items
            .Where(item => item.Listings.Count > 1)
            .Select(item => item.SourceItemId)
            .ShouldBe(["item-multi-1", "item-multi-2"], ignoreOrder: true);
        result.Items.Single(item => item.SourceItemId == "item-multi-1").Listings.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllItems_ShouldDropNonNumericVariantListings_AsInternalSkulabsVariants()
    {
        const string json = """
                            [
                              {
                                "_id": "item-bad-1",
                                "name": "Bad 1",
                                "sku": "SKU-B1",
                                "upc": "UPC-B1",
                                "listings": [
                                  { "variant_id": "not-a-number", "item_id": "prod-a", "_id": "l-a" }
                                ]
                              },
                              {
                                "_id": "item-bad-2",
                                "name": "Bad 2",
                                "sku": "SKU-B2",
                                "upc": "UPC-B2",
                                "listings": [
                                  { "variant_id": "also-bad", "item_id": "prod-b", "_id": "l-b" }
                                ]
                              },
                              {
                                "_id": "item-good",
                                "name": "Good",
                                "sku": "SKU-G",
                                "upc": "UPC-G",
                                "listings": [
                                  { "variant_id": "42", "item_id": "prod-g", "_id": "l-g" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Items.Single(item => item.SourceItemId == "item-good")
            .Listings.Single().ShouldBe(new SkulabsApiListing("l-g", "42", "prod-g"));

        // The non-numeric listings are dropped, leaving those items with no Shopify listing at all.
        result.Items
            .Where(item => item.Listings.Count == 0)
            .Select(item => item.SourceItemId)
            .ShouldBe(["item-bad-1", "item-bad-2"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetAllItems_ShouldDropNullVariantListings_AsInternalSkulabsVariants()
    {
        // SkuLabs sends variant_id as JSON null on internal (non-Shopify) listings, e.g. LINE_SKU_*
        // placeholder items. These can never match a Shopify variant and are dropped entirely.
        const string json = """
                            [
                              {
                                "_id": "item-null",
                                "name": "Null Listing",
                                "sku": "SKU-N",
                                "upc": "UPC-N",
                                "listings": [
                                  { "variant_id": null, "item_id": "LINE_SKU_11-LPK", "_id": "l-null" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        var stored = result.Items.ShouldHaveSingleItem();
        stored.SourceItemId.ShouldBe("item-null");
        stored.Listings.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllItems_ShouldThrow_WhenResponseStatusIsNotSuccess()
    {
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut();

        await Should.ThrowAsync<SkulabsRequestFailedException>(() => sut.GetAllItems());
    }

    [Fact]
    public async Task UpdateItems_ShouldSendPutRequestToBulkUpsertEndpoint_WithItemsArrayInBody()
    {
        _handler.SetResponse(JsonResponse("""{"success":true}"""));
        var sut = CreateSut();

        await sut.UpdateItems([
            new SkulabsItemUpdateWithId("item-1", "First"),
            new SkulabsItemUpdateWithId("item-2", "Second"),
        ]);

        _handler.Requests.Count.ShouldBe(1);
        var request = _handler.Requests[0];
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsoluteUri.ShouldBe($"{BaseUrl}item/bulk_upsert");
        request.Content.ShouldNotBeNull();
        request.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var body = _handler.RequestBodies[0];
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("items");
        items.GetArrayLength().ShouldBe(2);
        items[0].GetProperty("_id").GetString().ShouldBe("item-1");
        items[0].GetProperty("name").GetString().ShouldBe("First");
        items[1].GetProperty("_id").GetString().ShouldBe("item-2");
        items[1].GetProperty("name").GetString().ShouldBe("Second");
    }

    [Fact]
    public async Task UpdateItems_ShouldSendEmptyItemsArray_WhenInputIsEmpty()
    {
        _handler.SetResponse(JsonResponse("""{"success":true}"""));
        var sut = CreateSut();

        await sut.UpdateItems([]);

        var body = _handler.RequestBodies[0];
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task UpdateItems_ShouldThrow_WhenResponseStatusIsNotSuccess()
    {
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut();

        await Should.ThrowAsync<SkulabsRequestFailedException>(() =>
            sut.UpdateItems([new SkulabsItemUpdateWithId("item-1", "Name")]));
    }

    [Fact]
    public async Task UpdateItems_ShouldLogStructuredErrorFields_WhenResponseIsStandardSkulabsErrorEnvelope()
    {
        const string errorBody = """
                                 {
                                   "error": {
                                     "message": "Item not found",
                                     "statusCode": 404,
                                     "code": "ITEM_MISSING",
                                     "overview": "Lookup failed",
                                     "origin": "items-service",
                                     "skulabsTraceId": "trace-xyz-789",
                                     "user_error": true
                                   }
                                 }
                                 """;
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
        });
        var sut = CreateSut();

        await Should.ThrowAsync<SkulabsRequestFailedException>(() =>
            sut.UpdateItems([new SkulabsItemUpdateWithId("item-1", "Name")]));

        var errorEntry = _logger.Entries.SingleOrDefault(e => e.LogLevel == LogLevel.Error);
        errorEntry.ShouldNotBeNull();
        errorEntry.Message.ShouldContain("ITEM_MISSING");
        errorEntry.Message.ShouldContain("Item not found");
        errorEntry.Message.ShouldContain("trace-xyz-789");
        errorEntry.Message.ShouldContain("items-service");
        errorEntry.Message.ShouldContain("item/bulk_upsert");
    }

    [Fact]
    public async Task UpdateItems_ShouldLogInformation_OnSuccess()
    {
        _handler.SetResponse(JsonResponse("""{"success":true}"""));
        var sut = CreateSut();

        await sut.UpdateItems([
            new SkulabsItemUpdateWithId("item-a", "A"),
            new SkulabsItemUpdateWithId("item-b", "B"),
        ]);

        _logger.Entries.ShouldContain(e =>
            e.LogLevel == LogLevel.Information && e.Message.Contains("2"));
        _logger.Entries.ShouldNotContain(e => e.LogLevel == LogLevel.Error);
    }

    [Fact]
    public async Task UpdateItem_Extension_ShouldDelegateToUpdateItems_WithSingletonArray()
    {
        _handler.SetResponse(JsonResponse("""{"success":true}"""));
        var sut = CreateSut();

        await sut.UpdateItem("item-42", new SkulabsItemUpdate("New Name"));

        _handler.Requests.Count.ShouldBe(1);
        var request = _handler.Requests[0];
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsoluteUri.ShouldBe($"{BaseUrl}item/bulk_upsert");

        var body = _handler.RequestBodies[0];
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("items");
        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("_id").GetString().ShouldBe("item-42");
        items[0].GetProperty("name").GetString().ShouldBe("New Name");
    }

    private SkulabsItemClient CreateSut()
    {
        var httpClient = new HttpClient(_handler);
        return new SkulabsItemClient(httpClient, _options, _rateLimitService, _logger);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];
        private HttpResponseMessage? _response;

        public void SetResponse(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _response ?? SuccessFor(request);
        }

        /// <summary>
        /// Reads and writes succeed with different shapes — an item array versus an
        /// acknowledgement — so one fixed default would make every test of the other kind fail.
        /// Tests that care about the body set their own response.
        /// </summary>
        private static HttpResponseMessage SuccessFor(HttpRequestMessage request) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.Method == HttpMethod.Put ? """{"success":true}""" : "[]",
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
