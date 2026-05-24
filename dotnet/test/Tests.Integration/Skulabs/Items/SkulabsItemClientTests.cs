using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    private readonly IOptionsMonitor<SkulabsApiOptions> _options =
        Substitute.For<IOptionsMonitor<SkulabsApiOptions>>();

    private readonly TestLogger<SkulabsItemClient> _logger = new();
    private readonly RecordingHttpMessageHandler _handler = new();

    public SkulabsItemClientTests()
    {
        _options.CurrentValue.Returns(new SkulabsApiOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = ApiKey
        });
    }

    [Fact]
    public void Constructor_ShouldConfigureBaseAddressAndAuthorizationHeader()
    {
        var httpClient = new HttpClient(_handler);

        _ = new SkulabsItemClient(httpClient, _options, _logger);

        httpClient.BaseAddress.ShouldBe(new Uri(BaseUrl));
        httpClient.DefaultRequestHeaders.Authorization.ShouldBe(
            new AuthenticationHeaderValue("Bearer", ApiKey));
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
            .ShouldContain("\"_id\": 1, \"name\": 1, \"sku\": 1, \"upc\": 1, \"listings\": 1");
    }

    [Fact]
    public async Task GetAllItems_ShouldMapResponse_AndFilterOutItemsWithoutListings()
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

        result.Length.ShouldBe(2);
        result[0].ShouldBe(new SkuLabsItem("item-1", "listing-1", 1, "SKU-1", "UPC-1", "Item One"));
        result[1].ShouldBe(new SkuLabsItem("item-3", "listing-3", 3, "SKU-3", "UPC-3", "Item Three"));
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

        await Should.ThrowAsync<HttpRequestException>(() => sut.GetAllItems());

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

        await Should.ThrowAsync<HttpRequestException>(() => sut.GetAllItems());

        var errorEntry = _logger.Entries.SingleOrDefault(e => e.LogLevel == LogLevel.Error);
        errorEntry.ShouldNotBeNull();
        errorEntry.Message.ShouldContain("Bad Gateway");
    }

    [Fact]
    public async Task GetAllItems_ShouldFilterOutItemsWithMultipleListings_AndLogSingleAggregateWarning()
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

        result.Length.ShouldBe(1);
        result[0].ShouldBe(new SkuLabsItem("item-single", "l-s", 30, "SKU-S", "UPC-S", "Single"));

        var warnings = _logger.Entries
            .Where(e => e.LogLevel == LogLevel.Warning && e.Message.Contains("multiple listings"))
            .ToArray();
        warnings.Length.ShouldBe(1);
        warnings[0].Message.ShouldContain("2");
    }

    [Fact]
    public async Task GetAllItems_ShouldNotEmitMultipleListingsWarning_WhenAllItemsHaveOneListing()
    {
        const string json = """
                            [
                              {
                                "_id": "item-single",
                                "name": "Single",
                                "sku": "SKU-S",
                                "upc": "UPC-S",
                                "listings": [
                                  { "variant_id": "1", "item_id": "prod-s", "_id": "l-s" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        await sut.GetAllItems();

        _logger.Entries.ShouldNotContain(e =>
            e.LogLevel == LogLevel.Warning && e.Message.Contains("multiple listings"));
    }

    [Fact]
    public async Task GetAllItems_ShouldFilterOutItemsWithNonNumericVariantId_AndLogSingleAggregateWarning()
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

        result.Length.ShouldBe(1);
        result[0].ShouldBe(new SkuLabsItem("item-good", "l-g", 42, "SKU-G", "UPC-G", "Good"));

        var warnings = _logger.Entries
            .Where(e => e.LogLevel == LogLevel.Warning && e.Message.Contains("non-numeric"))
            .ToArray();
        warnings.Length.ShouldBe(1);
        warnings[0].Message.ShouldContain("2");
        warnings[0].Message.ShouldContain("ID");
    }

    [Fact]
    public async Task GetAllItems_ShouldNotEmitNonNumericWarning_WhenAllVariantIdsAreNumeric()
    {
        const string json = """
                            [
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

        await sut.GetAllItems();

        _logger.Entries.ShouldNotContain(e =>
            e.LogLevel == LogLevel.Warning && e.Message.Contains("non-numeric"));
    }

    [Fact]
    public async Task GetAllItems_ShouldThrow_WhenResponseStatusIsNotSuccess()
    {
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut();

        await Should.ThrowAsync<HttpRequestException>(() => sut.GetAllItems());
    }

    [Fact]
    public async Task UpdateItem_ShouldSendPutRequestToItemUpdateEndpoint_WithItemIdAndNameInBody()
    {
        _handler.SetResponse(JsonResponse("{}"));
        var sut = CreateSut();

        await sut.UpdateItem("item-42", new SkulabsItemUpdate("New Name"));

        _handler.Requests.Count.ShouldBe(1);
        var request = _handler.Requests[0];
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsoluteUri.ShouldBe($"{BaseUrl}item/update");
        request.Content.ShouldNotBeNull();
        request.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var body = _handler.RequestBodies[0];
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("item_id").GetString().ShouldBe("item-42");
        document.RootElement.GetProperty("name").GetString().ShouldBe("New Name");
    }

    [Fact]
    public async Task UpdateItem_ShouldThrow_WhenResponseStatusIsNotSuccess()
    {
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut();

        await Should.ThrowAsync<HttpRequestException>(() =>
            sut.UpdateItem("item-1", new SkulabsItemUpdate("Name")));
    }

    [Fact]
    public async Task UpdateItem_ShouldLogStructuredErrorFields_WhenResponseIsStandardSkulabsErrorEnvelope()
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

        await Should.ThrowAsync<HttpRequestException>(() =>
            sut.UpdateItem("item-1", new SkulabsItemUpdate("Name")));

        var errorEntry = _logger.Entries.SingleOrDefault(e => e.LogLevel == LogLevel.Error);
        errorEntry.ShouldNotBeNull();
        errorEntry.Message.ShouldContain("ITEM_MISSING");
        errorEntry.Message.ShouldContain("Item not found");
        errorEntry.Message.ShouldContain("trace-xyz-789");
        errorEntry.Message.ShouldContain("items-service");
        errorEntry.Message.ShouldContain("item/update");
    }

    [Fact]
    public async Task UpdateItem_ShouldLogInformation_OnSuccess()
    {
        _handler.SetResponse(JsonResponse("{}"));
        var sut = CreateSut();

        await sut.UpdateItem("item-99", new SkulabsItemUpdate("Updated"));

        _logger.Entries.ShouldContain(e =>
            e.LogLevel == LogLevel.Information && e.Message.Contains("item-99"));
        _logger.Entries.ShouldNotContain(e => e.LogLevel == LogLevel.Error);
    }

    private SkulabsItemClient CreateSut()
    {
        var httpClient = new HttpClient(_handler);
        return new SkulabsItemClient(httpClient, _options, _logger);
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
        private HttpResponseMessage _response = new(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };

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
            return _response;
        }
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
