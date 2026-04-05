using Application.Shopify;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Shopify;

public class ShopifySyncServiceTests : IDisposable
{
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ShopifySyncService> _logger = new();

    public ShopifySyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldCreateVariant_WhenVariantNotInDatabase()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "Blue T-Shirt",
                "Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Count.ShouldBe(1);
        variants[0].GlobalProductId.ShouldBe("gid://shopify/Product/100");
        variants[0].GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/200");
        variants[0].ProductId.ShouldBe(100L);
        variants[0].VariantId.ShouldBe(200L);
        variants[0].ProductTitle.ShouldBe("Blue T-Shirt");
        variants[0].VariantTitle.ShouldBe("Large");
        variants[0].Sku.ShouldBe("SKU-1");
        variants[0].Barcode.ShouldBe("BAR-1");
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldUpdateTitle_WhenTitleDiffersFromDatabase()
    {
        var existingVariant = SeedVariant("gid://shopify/ProductVariant/200", title: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "New Title",
                "",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.ProductTitle.ShouldBe("New Title");
        updated.UpdatedOnUtc.ShouldBeGreaterThanOrEqualTo(existingVariant.UpdatedOnUtc);
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldUpdateVariantTitle_WhenVariantTitleDiffersFromDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", variantTitle: "Small", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.VariantTitle.ShouldBe("Large");
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldNotUpdateSku_WhenSkuAlreadySetInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "OLD-SKU", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "NEW-SKU",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Sku.ShouldBe("OLD-SKU");
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldUpdateSku_WhenSkuIsEmptyInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "NEW-SKU",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Sku.ShouldBe("NEW-SKU");
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldNotUpdateBarcode_WhenBarcodeAlreadySetInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "SKU-1", barcode: "OLD-BAR");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "SKU-1",
                "NEW-BAR")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Barcode.ShouldBe("OLD-BAR");
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldUpdateBarcode_WhenBarcodeIsEmptyInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "SKU-1", barcode: "");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "SKU-1",
                "NEW-BAR")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Barcode.ShouldBe("NEW-BAR");
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldNotUpdateVariant_WhenAllFieldsMatch()
    {
        var existingVariant = SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", variantTitle: "Large", sku: "SKU-1", barcode: "BAR-1");
        var originalUpdatedOn = existingVariant.UpdatedOnUtc;
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var variant = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        variant.UpdatedOnUtc.ShouldBe(originalUpdatedOn);
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldSetUpdatedOnUtc_WhenVariantIsUpdated()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        SeedVariant("gid://shopify/ProductVariant/200", title: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "New Title",
                "",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.UpdatedOnUtc.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldHandleMixedCreateAndUpdate()
    {
        SeedVariant("gid://shopify/ProductVariant/100", title: "Existing", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/10",
                "gid://shopify/ProductVariant/100",
                "Updated Title",
                "",
                "SKU-A",
                "BAR-A"),
            new ShopifyProductVariant(
                "gid://shopify/Product/20",
                "gid://shopify/ProductVariant/200",
                "New Variant",
                "",
                "SKU-B",
                "BAR-B")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Count.ShouldBe(2);

        var existingVariant = variants.Single(v => v.GlobalVariantId == "gid://shopify/ProductVariant/100");
        existingVariant.ProductTitle.ShouldBe("Updated Title");

        var newVariant = variants.Single(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        newVariant.ProductTitle.ShouldBe("New Variant");
        newVariant.Sku.ShouldBe("SKU-B");
        newVariant.Barcode.ShouldBe("BAR-B");
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldLogErrorAndRethrow_WhenShopifyCallFails()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _shopifyProductService.GetProducts().ThrowsAsync(exception);

        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => sut.SynchronizeProducts());

        thrown.ShouldBeSameAs(exception);

        var errorLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errorLogs.Length.ShouldBe(1);
        errorLogs[0].Exception.ShouldBeSameAs(exception);
    }

    [Fact]
    public async Task SynchronizeProducts_ShouldLogDebugStatements_DuringSuccessfulSync()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.SynchronizeProducts();

        var debugLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Debug).ToArray();
        debugLogs.Length.ShouldBeGreaterThan(0);
    }

    private ShopifyProductVariantEntity SeedVariant(
        string globalVariantId,
        string globalProductId = "gid://shopify/Product/100",
        string title = "Variant",
        string variantTitle = "",
        string sku = "SKU",
        string barcode = "BAR")
    {
        var fullTitle = string.IsNullOrWhiteSpace(variantTitle) ? title : $"{title} ({variantTitle})";
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = globalProductId,
            ProductId = 100,
            GlobalVariantId = globalVariantId,
            VariantId = 200,
            ProductTitle = title,
            VariantTitle = variantTitle,
            FullTitle = fullTitle,
            Sku = sku,
            Barcode = barcode
        };

        _dbContext.Set<ShopifyProductVariantEntity>().Add(entity);
        return entity;
    }

    private ShopifySyncService CreateSut() => new(_shopifyProductService, _dbContext, _logger);

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
