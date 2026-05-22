using Application;
using Application.Products.Events;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using NSubstitute;
using Shouldly;

namespace Tests.Application.Products.Events;

public class ProductVariantUpdatedConsumerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly TestLogger<ProductVariantUpdatedConsumer> _logger = new();

    public ProductVariantUpdatedConsumerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task OnHandle_ShouldUpdateShopifyVariant_WhenFeatureFlagEnabled()
    {
        var entity = SeedVariant(
            globalProductId: "gid://shopify/Product/100",
            globalVariantId: "gid://shopify/ProductVariant/200",
            sku: "SKU-200",
            barcode: "BAR-200");
        await _dbContext.SaveChangesAsync();
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(true);

        await CreateSut().OnHandle(
            new ProductVariantUpdatedEvent(entity.ShopifyProductVariantId),
            CancellationToken.None);

        await _shopifyProductService.Received(1).UpdateVariants(
            "gid://shopify/Product/100",
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(variants =>
                variants.Count() == 1 &&
                variants.Single().GlobalVariantId == "gid://shopify/ProductVariant/200" &&
                variants.Single().Sku == "SKU-200" &&
                variants.Single().Barcode == "BAR-200"));
    }

    [Fact]
    public async Task OnHandle_ShouldSkipShopifyUpdate_WhenFeatureFlagDisabled()
    {
        var entity = SeedVariant(
            globalProductId: "gid://shopify/Product/100",
            globalVariantId: "gid://shopify/ProductVariant/200",
            sku: "SKU-200",
            barcode: "BAR-200");
        await _dbContext.SaveChangesAsync();
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(false);

        await CreateSut().OnHandle(
            new ProductVariantUpdatedEvent(entity.ShopifyProductVariantId),
            CancellationToken.None);

        await _shopifyProductService.DidNotReceive().UpdateVariants(
            Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
        _logger.Entries.Any(e =>
            e.LogLevel == LogLevel.Information &&
            e.Message.Contains("ShopifyWriteBack feature flag is disabled")).ShouldBeTrue();
    }

    [Fact]
    public async Task OnHandle_ShouldLogWarningAndSkipShopifyUpdate_WhenVariantNotFound()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(true);

        await CreateSut().OnHandle(
            new ProductVariantUpdatedEvent(Guid.NewGuid()),
            CancellationToken.None);

        await _shopifyProductService.DidNotReceive().UpdateVariants(
            Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
        _logger.Entries.Any(e =>
            e.LogLevel == LogLevel.Warning &&
            e.Message.Contains("not found in the database")).ShouldBeTrue();
    }

    private ProductVariantUpdatedConsumer CreateSut() =>
        new(_dbContext, _shopifyProductService, _featureManager, _logger);

    private ShopifyProductVariantEntity SeedVariant(string globalProductId, string globalVariantId,
        string sku, string barcode)
    {
        var entity = new ShopifyProductVariantEntity
        {
            GlobalProductId = globalProductId,
            ProductId = 100,
            GlobalVariantId = globalVariantId,
            VariantId = 200,
            DisplayName = "Test Variant",
            Sku = sku,
            Barcode = barcode
        };
        _dbContext.ShopifyProductVariants.Add(entity);
        return entity;
    }

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
