using Integration.Shopify.GraphQl;
using Integration.Shopify.Products;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ShopifySharp.GraphQL;
using Shouldly;

namespace Tests.Integration.Shopify.Products;

public class ShopifyProductServiceTests
{
    private readonly IShopifyGraphQlService _graphQlService = Substitute.For<IShopifyGraphQlService>();
    private readonly TestLogger<ShopifyProductService> _logger = new();

    [Fact]
    public async Task GetProducts_ShouldMapProductsAndVariants_FromSinglePage()
    {
        _graphQlService.ExecuteAsync<GetAllProductsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(CreateResponse(
                hasNextPage: false,
                endCursor: null,
                CreateProduct(
                    id: "gid://shopify/Product/100",
                    title: "Basic Tee",
                    CreateVariant(
                        id: "gid://shopify/ProductVariant/200",
                        title: "Default Title",
                        sku: "SKU-1",
                        barcode: "BAR-1"),
                    CreateVariant(
                        id: "gid://shopify/ProductVariant/201",
                        title: "Large",
                        sku: null,
                        barcode: null)),
                CreateProduct(
                    id: null,
                    title: null,
                    CreateVariant(
                        id: null,
                        title: null,
                        sku: null,
                        barcode: null))));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.GetProducts();

        result.Length.ShouldBe(3);
        result[0].ShouldBe(new ShopifyProductVariant(
            "gid://shopify/Product/100",
            "gid://shopify/ProductVariant/200",
            "Basic Tee",
            "SKU-1",
            "BAR-1"));
        result[1].ShouldBe(new ShopifyProductVariant(
            "gid://shopify/Product/100",
            "gid://shopify/ProductVariant/201",
            "Large",
            string.Empty,
            string.Empty));
        result[2].ShouldBe(new ShopifyProductVariant(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty));

        await _graphQlService.Received(1).ExecuteAsync<GetAllProductsGraphResponse>(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, object?>?>(variables =>
                variables != null &&
                variables.Count == 1 &&
                variables["after"] == null));
    }

    [Fact]
    public async Task GetProducts_ShouldRequestNextPageUsingEndCursor_AndCombineResults()
    {
        _graphQlService.ExecuteAsync<GetAllProductsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(
                CreateResponse(
                    hasNextPage: true,
                    endCursor: "cursor-1",
                    CreateProduct(
                        id: "gid://shopify/Product/1",
                        title: "First product",
                        CreateVariant(
                            id: "gid://shopify/ProductVariant/11",
                            title: "Default Title",
                            sku: "FIRST",
                            barcode: "111"))),
                CreateResponse(
                    hasNextPage: false,
                    endCursor: "cursor-2",
                    CreateProduct(
                        id: "gid://shopify/Product/2",
                        title: "Second product",
                        CreateVariant(
                            id: "gid://shopify/ProductVariant/22",
                            title: "Blue",
                            sku: "SECOND",
                            barcode: "222"))));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.GetProducts();

        result.Select(x => x.VariantId).ShouldBe([11, 22]);
        await _graphQlService.Received(1).ExecuteAsync<GetAllProductsGraphResponse>(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, object?>?>(variables =>
                variables != null &&
                variables["after"] == null));
        await _graphQlService.Received(1).ExecuteAsync<GetAllProductsGraphResponse>(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, object?>?>(variables =>
                variables != null &&
                (string?)variables["after"] == "cursor-1"));
    }

    [Fact]
    public async Task GetProducts_ShouldLogErrorAndRethrow_WhenGraphQlCallFails()
    {
        var exception = new InvalidOperationException("boom");
        _graphQlService.ExecuteAsync<GetAllProductsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns<Task<GetAllProductsGraphResponse>>(_ => throw exception);

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var action = () => sut.GetProducts();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(action);

        thrown.ShouldBeSameAs(exception);
        var errorLogs = _logger.Entries.Where(entry => entry.LogLevel == LogLevel.Error).ToArray();

        errorLogs.Length.ShouldBe(1);
        errorLogs[0].Message.ShouldBe("Failed to fetch products from Shopify.");
        errorLogs[0].Exception.ShouldBeSameAs(exception);
    }

    private static GetAllProductsGraphResponse CreateResponse(bool hasNextPage, string? endCursor, params Product[] products)
    {
        return new GetAllProductsGraphResponse
        {
            Products = new ProductConnection
            {
                nodes = products,
                pageInfo = new PageInfo(null, endCursor, false, hasNextPage)
            }
        };
    }

    private static Product CreateProduct(string? id, string? title, params ProductVariant[] variants)
    {
        return new Product
        {
            id = id,
            title = title,
            variants = new ProductVariantConnection
            {
                nodes = variants
            }
        };
    }

    private static ProductVariant CreateVariant(string? id, string? title, string? sku, string? barcode)
    {
        return new ProductVariant
        {
            id = id,
            title = title,
            sku = sku,
            barcode = barcode
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
