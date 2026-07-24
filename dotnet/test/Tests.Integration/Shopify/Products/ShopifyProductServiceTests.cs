using Integration.Shopify.GraphQl;
using Integration.Shopify.Products;
using Integration.Shopify.Responses;
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
        _graphQlService.ExecuteAsync<GetAllProductVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(CreateResponse(
                hasNextPage: false,
                endCursor: null,
                CreateVariant(
                    id: "gid://shopify/ProductVariant/200",
                    productId: "gid://shopify/Product/100",
                    productTitle: "Basic Tee",
                    title: "Default Title",
                    sku: "SKU-1",
                    barcode: "BAR-1"),
                CreateVariant(
                    id: "gid://shopify/ProductVariant/201",
                    productId: "gid://shopify/Product/100",
                    productTitle: "Basic Tee",
                    title: "Large",
                    sku: null,
                    barcode: null),
                CreateVariant(
                    id: null,
                    productId: null,
                    productTitle: null,
                    title: null,
                    sku: null,
                    barcode: null)));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.GetProducts();

        result.Length.ShouldBe(3);
        result[0].GlobalProductId.ShouldBe("gid://shopify/Product/100");
        result[0].GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/200");
        result[0].DisplayName.ShouldBe("Basic Tee");
        result[0].Sku.ShouldBe("SKU-1");
        result[0].Barcode.ShouldBe("BAR-1");

        result[1].GlobalProductId.ShouldBe("gid://shopify/Product/100");
        result[1].GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/201");
        result[1].DisplayName.ShouldBe("Basic Tee (Large)");
        result[1].Sku.ShouldBe(string.Empty);
        result[1].Barcode.ShouldBe(string.Empty);

        result[2].GlobalProductId.ShouldBe(string.Empty);
        result[2].GlobalVariantId.ShouldBe(string.Empty);
        result[2].DisplayName.ShouldBe(string.Empty);
        result[2].Sku.ShouldBe(string.Empty);
        result[2].Barcode.ShouldBe(string.Empty);

        await _graphQlService.Received(1).ExecuteAsync<GetAllProductVariantsGraphResponse>(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, object?>?>(variables =>
                variables != null &&
                variables.Count == 1 &&
                variables["after"] == null));
    }

    [Fact]
    public async Task GetProducts_ShouldRequestNextPageUsingEndCursor_AndCombineResults()
    {
        _graphQlService.ExecuteAsync<GetAllProductVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(
                CreateResponse(
                    hasNextPage: true,
                    endCursor: "cursor-1",
                    CreateVariant(
                        id: "gid://shopify/ProductVariant/11",
                        productId: "gid://shopify/Product/1",
                        productTitle: "First product",
                        title: "Default Title",
                        sku: "FIRST",
                        barcode: "111")),
                CreateResponse(
                    hasNextPage: false,
                    endCursor: "cursor-2",
                    CreateVariant(
                        id: "gid://shopify/ProductVariant/22",
                        productId: "gid://shopify/Product/2",
                        productTitle: "Second product",
                        title: "Blue",
                        sku: "SECOND",
                        barcode: "222")));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.GetProducts();

        result.Select(x => x.VariantId).ShouldBe([11, 22]);
        await _graphQlService.Received(1).ExecuteAsync<GetAllProductVariantsGraphResponse>(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, object?>?>(variables =>
                variables != null &&
                variables["after"] == null));
        await _graphQlService.Received(1).ExecuteAsync<GetAllProductVariantsGraphResponse>(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, object?>?>(variables =>
                variables != null &&
                (string?)variables["after"] == "cursor-1"));
    }

    [Fact]
    public async Task GetProducts_ShouldPropagateException_WhenGraphQlCallFails()
    {
        // The fetch must NOT swallow failures into an empty result: the full sync relies on an
        // empty set meaning "no variants in Shopify" so it can safely mark absent variants deleted.
        var exception = new InvalidOperationException("boom");
        _graphQlService.ExecuteAsync<GetAllProductVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns<Task<GetAllProductVariantsGraphResponse>>(_ => throw exception);

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => sut.GetProducts());
        thrown.ShouldBeSameAs(exception);
    }

    [Fact]
    public async Task UpdateVariants_ShouldReturnTrue_WhenUpdateSucceeds()
    {
        _graphQlService.ExecuteAsync<UpdateVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(new UpdateVariantsGraphResponse(UserErrors: null));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.UpdateVariants("gid://shopify/Product/100",
        [
            new ShopifyUpdateProductVariant("gid://shopify/ProductVariant/200", "SKU-1", "BAR-1")
        ]);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateVariants_ShouldLogDebug_WhenUpdateSucceeds()
    {
        _graphQlService.ExecuteAsync<UpdateVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(new UpdateVariantsGraphResponse(UserErrors: null));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        await sut.UpdateVariants("gid://shopify/Product/100",
        [
            new ShopifyUpdateProductVariant("gid://shopify/ProductVariant/200", "SKU-1", "BAR-1")
        ]);

        _logger.Entries.Where(e => e.LogLevel == LogLevel.Debug).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task UpdateVariants_ShouldReturnFalse_AndLogError_WhenUserErrorsArePresent()
    {
        _graphQlService.ExecuteAsync<UpdateVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(new UpdateVariantsGraphResponse(UserErrors:
            [
                new UserErrorsResponse("Invalid SKU", "sku")
            ]));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.UpdateVariants("gid://shopify/Product/100",
        [
            new ShopifyUpdateProductVariant("gid://shopify/ProductVariant/200", "SKU-1", "BAR-1")
        ]);

        result.ShouldBeFalse();
        var errorLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errorLogs.Length.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateVariants_ShouldReturnFalse_AndLogError_WhenGraphQlThrows()
    {
        var exception = new InvalidOperationException("GraphQL error");
        _graphQlService.ExecuteAsync<UpdateVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns<Task<UpdateVariantsGraphResponse>>(_ => throw exception);

        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.UpdateVariants("gid://shopify/Product/100",
        [
            new ShopifyUpdateProductVariant("gid://shopify/ProductVariant/200", "SKU-1", "BAR-1")
        ]);

        result.ShouldBeFalse();
        var errorLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errorLogs.Length.ShouldBe(1);
        errorLogs[0].Exception.ShouldBeSameAs(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateVariants_ShouldThrow_WhenProductIdIsNullOrWhitespace(string productId)
    {
        var sut = new ShopifyProductService(_graphQlService, _logger);

        await Should.ThrowAsync<ArgumentException>(() => sut.UpdateVariants(productId,
        [
            new ShopifyUpdateProductVariant("gid://shopify/ProductVariant/200", "SKU-1", "BAR-1")
        ]));
    }

    [Fact]
    public async Task UpdateVariants_ShouldReturnTrue_AndNotCallGraphQl_WhenVariantsIsEmpty()
    {
        var sut = new ShopifyProductService(_graphQlService, _logger);

        var result = await sut.UpdateVariants("gid://shopify/Product/100", []);

        result.ShouldBeTrue();
        await _graphQlService.DidNotReceive().ExecuteAsync<UpdateVariantsGraphResponse>(
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task UpdateVariants_ShouldPassCorrectProductId_ToGraphQl()
    {
        _graphQlService.ExecuteAsync<UpdateVariantsGraphResponse>(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>?>())
            .Returns(new UpdateVariantsGraphResponse(UserErrors: null));

        var sut = new ShopifyProductService(_graphQlService, _logger);

        await sut.UpdateVariants("gid://shopify/Product/100",
        [
            new ShopifyUpdateProductVariant("gid://shopify/ProductVariant/200", "SKU-1", "BAR-1"),
            new ShopifyUpdateProductVariant("gid://shopify/ProductVariant/201", "SKU-2", "BAR-2")
        ]);

        await _graphQlService.Received(1).ExecuteAsync<UpdateVariantsGraphResponse>(
            Arg.Any<string>(),
            Arg.Is<IDictionary<string, object?>?>(variables =>
                variables != null &&
                (string?)variables["productId"] == "gid://shopify/Product/100"));
    }

    private static GetAllProductVariantsGraphResponse CreateResponse(bool hasNextPage, string? endCursor, params ProductVariant[] variants)
    {
        return new GetAllProductVariantsGraphResponse
        {
            ProductVariants = new ProductVariantConnection
            {
                nodes = variants,
                pageInfo = new PageInfo(null, endCursor, false, hasNextPage)
            }
        };
    }

    private static ProductVariant CreateVariant(
        string? id, string? productId, string? productTitle, string? title, string? sku, string? barcode)
    {
        return new ProductVariant
        {
            id = id,
            title = title,
            sku = sku,
            barcode = barcode,
            product = new Product
            {
                id = productId,
                title = productTitle
            }
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
