using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
                                "sku": "SKU-1",
                                "upc": "UPC-1",
                                "listings": [
                                  { "variant_id": "var-1", "item_id": "prod-1", "_id": "listing-1" }
                                ]
                              },
                              {
                                "_id": "item-2",
                                "sku": "SKU-2",
                                "upc": "UPC-2",
                                "listings": []
                              },
                              {
                                "_id": "item-3",
                                "sku": "SKU-3",
                                "upc": "UPC-3",
                                "listings": [
                                  { "variant_id": "var-3", "item_id": "prod-3", "_id": "listing-3" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Length.ShouldBe(2);
        result[0].ShouldBe(new SkuLabsItem("item-1", "var-1", "prod-1", "SKU-1", "UPC-1"));
        result[1].ShouldBe(new SkuLabsItem("item-3", "var-3", "prod-3", "SKU-3", "UPC-3"));
    }

    [Fact]
    public async Task GetAllItems_ShouldReturnEmptyArray_AndLogWarning_WhenResponseBodyIsJsonNull()
    {
        _handler.SetResponse(JsonResponse("null"));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.ShouldBeEmpty();
        _logger.Entries.ShouldContain(entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Message.Contains("Response from SkuLabs API was empty"));
    }

    [Fact]
    public async Task GetAllItems_ShouldReturnEmptyArray_AndLogError_WhenResponseIsNotValidJson()
    {
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        });
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.ShouldBeEmpty();
        _logger.Entries.Any(entry =>
            entry.LogLevel == LogLevel.Error &&
            entry.Exception != null &&
            entry.Message.Contains("Failed to parse SkuLabs item response")).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllItems_ShouldLogWarning_ForEachItemWithMultipleListings()
    {
        const string json = """
                            [
                              {
                                "_id": "item-multi",
                                "sku": "SKU-M",
                                "upc": "UPC-M",
                                "listings": [
                                  { "variant_id": "var-a", "item_id": "prod-a", "_id": "l-a" },
                                  { "variant_id": "var-b", "item_id": "prod-b", "_id": "l-b" }
                                ]
                              },
                              {
                                "_id": "item-single",
                                "sku": "SKU-S",
                                "upc": "UPC-S",
                                "listings": [
                                  { "variant_id": "var-c", "item_id": "prod-c", "_id": "l-c" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Length.ShouldBe(2);
        var warnings = _logger.Entries
            .Where(e => e.LogLevel == LogLevel.Warning && e.Message.Contains("multiple listings"))
            .ToArray();
        warnings.Length.ShouldBe(1);
        warnings[0].Message.ShouldContain("item-multi");
    }

    [Fact]
    public async Task GetAllItems_ShouldUseFirstListing_WhenItemHasMultipleListings()
    {
        const string json = """
                            [
                              {
                                "_id": "item-multi",
                                "sku": "SKU-M",
                                "upc": "UPC-M",
                                "listings": [
                                  { "variant_id": "var-first", "item_id": "prod-first", "_id": "l-1" },
                                  { "variant_id": "var-second", "item_id": "prod-second", "_id": "l-2" }
                                ]
                              }
                            ]
                            """;
        _handler.SetResponse(JsonResponse(json));
        var sut = CreateSut();

        var result = await sut.GetAllItems();

        result.Length.ShouldBe(1);
        result[0].ShouldBe(new SkuLabsItem("item-multi", "var-first", "prod-first", "SKU-M", "UPC-M"));
    }

    [Fact]
    public async Task GetAllItems_ShouldThrow_WhenResponseStatusIsNotSuccess()
    {
        _handler.SetResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut();

        await Should.ThrowAsync<HttpRequestException>(() => sut.GetAllItems());
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
        private HttpResponseMessage _response = new(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };

        public void SetResponse(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_response);
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
