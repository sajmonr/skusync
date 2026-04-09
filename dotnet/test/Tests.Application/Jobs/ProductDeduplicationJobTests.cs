using Application.Products.Jobs;
using Application.Products.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using Shouldly;

namespace Tests.Application.Jobs;

public class ProductDeduplicationJobTests : IDisposable
{
    private readonly IProductsService _productsService = Substitute.For<IProductsService>();
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly ApplicationDbContext _dbContext;
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<ProductDeduplicationJob> _logger = new();

    public ProductDeduplicationJobTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Default: no duplicates found.
        _productsService.DeduplicateProducts().Returns(ProductDeduplicationResult.Success([]));
        // Default: Shopify update succeeds.
        _shopifyProductService.UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);
    }

    public void Dispose() => _dbContext.Dispose();

    // -------------------------------------------------------------------------
    // Service interaction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldCallDeduplicateProducts()
    {
        await CreateSut().Execute(_context);

        await _productsService.Received(1).DeduplicateProducts();
    }

    [Fact]
    public async Task Execute_ShouldNotCallShopify_WhenNoDuplicatesFound()
    {
        _productsService.DeduplicateProducts().Returns(ProductDeduplicationResult.Success([]));

        await CreateSut().Execute(_context);

        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldNotCallShopify_WhenDeduplicationFails()
    {
        _productsService.DeduplicateProducts()
            .Returns(ProductDeduplicationResult.Failure("DB unavailable"));

        await CreateSut().Execute(_context);

        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldCallUpdateVariants_WhenDuplicatesFound()
    {
        _productsService.DeduplicateProducts()
            .Returns(ProductDeduplicationResult.Success([100L, 200L]));

        SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10", variantId: 100L);
        SeedVariant("gid://shopify/ProductVariant/200", "gid://shopify/Product/10", variantId: 200L);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1)
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldCallUpdateVariants_OncePerProduct_WhenVariantsBelongToMultipleProducts()
    {
        _productsService.DeduplicateProducts()
            .Returns(ProductDeduplicationResult.Success([100L, 200L, 300L]));

        SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10", variantId: 100L);
        SeedVariant("gid://shopify/ProductVariant/200", "gid://shopify/Product/10", variantId: 200L);
        SeedVariant("gid://shopify/ProductVariant/300", "gid://shopify/Product/20", variantId: 300L);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1)
            .UpdateVariants("gid://shopify/Product/10", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
        await _shopifyProductService.Received(1)
            .UpdateVariants("gid://shopify/Product/20", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldPassCorrectVariantData_WhenCallingShopify()
    {
        _productsService.DeduplicateProducts()
            .Returns(ProductDeduplicationResult.Success([100L]));

        SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10", variantId: 100L, sku: "100", barcode: "100");
        await _dbContext.SaveChangesAsync();

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1).UpdateVariants(
            "gid://shopify/Product/10",
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(variants =>
                variants.Any(v =>
                    v.GlobalVariantId == "gid://shopify/ProductVariant/100" &&
                    v.Sku == "100" &&
                    v.Barcode == "100")));
    }

    // -------------------------------------------------------------------------
    // Exception handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldThrowJobExecutionException_WhenDeduplicateProductsThrows()
    {
        var exception = new InvalidOperationException("DB connection lost");
        _productsService.DeduplicateProducts().ThrowsAsync(exception);

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => CreateSut().Execute(_context));

        thrown.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public async Task Execute_ShouldNotRefireImmediately_WhenJobFails()
    {
        _productsService.DeduplicateProducts().ThrowsAsync(new InvalidOperationException());

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => CreateSut().Execute(_context));

        thrown.RefireImmediately.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldLogInformation_WhenJobStarts()
    {
        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("started"));
    }

    [Fact]
    public async Task Execute_ShouldLogInformation_WhenNoDuplicatesFound()
    {
        _productsService.DeduplicateProducts().Returns(ProductDeduplicationResult.Success([]));

        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e =>
            e.LogLevel == LogLevel.Information && e.Message.Contains("No duplicates found"));
    }

    [Fact]
    public async Task Execute_ShouldLogInformation_WhenJobCompletesSuccessfully()
    {
        _productsService.DeduplicateProducts()
            .Returns(ProductDeduplicationResult.Success([100L]));

        SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10", variantId: 100L);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public async Task Execute_ShouldLogError_WhenDeduplicationFails()
    {
        _productsService.DeduplicateProducts()
            .Returns(ProductDeduplicationResult.Failure("DB unavailable"));

        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Error && e.Message.Contains("DB unavailable"));
    }

    [Fact]
    public async Task Execute_ShouldLogError_WhenShopifyUpdateFails()
    {
        _productsService.DeduplicateProducts()
            .Returns(ProductDeduplicationResult.Success([100L]));

        SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10", variantId: 100L);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(false);

        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Error && e.Message.Contains("Failed to update"));
    }

    [Fact]
    public async Task Execute_ShouldLogError_WhenDeduplicateProductsThrows()
    {
        var exception = new InvalidOperationException("DB connection lost");
        _productsService.DeduplicateProducts().ThrowsAsync(exception);

        await Should.ThrowAsync<JobExecutionException>(() => CreateSut().Execute(_context));

        var errorLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errorLogs.Length.ShouldBe(1);
        errorLogs[0].Exception.ShouldBeSameAs(exception);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SeedVariant(
        string globalVariantId,
        string globalProductId,
        long variantId,
        string sku = "SKU",
        string barcode = "BAR")
    {
        _dbContext.Set<ShopifyProductVariantEntity>().Add(new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = globalProductId,
            ProductId = long.Parse(globalProductId.Split('/').Last()),
            GlobalVariantId = globalVariantId,
            VariantId = variantId,
            ProductTitle = "Product",
            VariantTitle = "",
            FullTitle = "Product",
            Sku = sku,
            Barcode = barcode
        });
    }

    private ProductDeduplicationJob CreateSut() =>
        new(_productsService, _shopifyProductService, _dbContext, _logger);

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
