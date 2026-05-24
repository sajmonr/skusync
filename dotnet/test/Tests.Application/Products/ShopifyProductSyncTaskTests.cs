using Application.Products.Maintenance;
using Application.Products.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Products;

public class ShopifyProductSyncTaskTests
{
    private readonly IProductsService _productsService = Substitute.For<IProductsService>();
    private readonly TestLogger<ShopifyProductSyncTask> _logger = new();

    [Fact]
    public async Task Execute_ShouldCallImportProducts()
    {
        var sut = CreateSut();

        await sut.Execute(CancellationToken.None);

        await _productsService.Received(1).ImportProductsFromShopify();
    }

    [Fact]
    public async Task Execute_ShouldCallDeduplicateProducts_WhenImportSucceeds()
    {
        _productsService.ImportProductsFromShopify().Returns(ProductImportResult.Success(0, 0));
        _productsService.DeduplicateProducts().Returns(ProductDeduplicationResult.Success([]));
        var sut = CreateSut();

        await sut.Execute(CancellationToken.None);

        await _productsService.Received(1).DeduplicateProducts();
    }

    [Fact]
    public async Task Execute_ShouldNotCallDeduplicateProducts_WhenImportFails()
    {
        _productsService.ImportProductsFromShopify()
            .Returns(ProductImportResult.Failure("Shopify unavailable"));
        var sut = CreateSut();

        await sut.Execute(CancellationToken.None);

        await _productsService.DidNotReceive().DeduplicateProducts();
    }

    [Fact]
    public async Task Execute_ShouldLogError_WhenImportFails()
    {
        _productsService.ImportProductsFromShopify()
            .Returns(ProductImportResult.Failure("Shopify unavailable"));
        var sut = CreateSut();

        await sut.Execute(CancellationToken.None);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Error && e.Message.Contains("Shopify unavailable"));
    }

    [Fact]
    public async Task Execute_ShouldPropagateException_WhenImportThrows()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _productsService.ImportProductsFromShopify().ThrowsAsync(exception);
        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => sut.Execute(CancellationToken.None));

        thrown.ShouldBeSameAs(exception);
    }

    [Fact]
    public void Name_ShouldReturnClassName()
    {
        CreateSut().Name.ShouldBe(nameof(ShopifyProductSyncTask));
    }

    private ShopifyProductSyncTask CreateSut() => new(_productsService, _logger);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
