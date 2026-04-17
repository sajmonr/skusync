using Application.Events;
using Application.Products.Events;
using Application.Products.Webhook;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Tests.Application.Queue;

public class ShopifyProductUpdateWebhookHandlerTests : IDisposable
{
    private readonly IEventDispatcher _eventDispatcher = Substitute.For<IEventDispatcher>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ShopifyProductUpdateWebhookHandler> _logger = new();

    public ShopifyProductUpdateWebhookHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    // -------------------------------------------------------------------------
    // New variant creation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCreateEntity_WhenVariantDoesNotExistInDatabase()
    {
        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.Count.ShouldBe(1);
        saved[0].VariantId.ShouldBe(200L);
    }

    [Fact]
    public async Task Handle_ShouldDispatchCreatedEvent_WhenNewVariantIsSaved()
    {
        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        _eventDispatcher.Received(1).Dispatch(
            Arg.Is<ProductChangedEvent>(e => e.ProductVariantId != Guid.Empty && e.ChangeType == ProductChangeType.Created));
    }

    // -------------------------------------------------------------------------
    // Display name updates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldUpdateDisplayName_WhenDisplayNameChangedInShopify()
    {
        SeedVariant(100, 200, displayName: "Old Title", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "New Title", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.DisplayName.ShouldBe("New Title");
    }

    // -------------------------------------------------------------------------
    // Updated event dispatching — barcode / SKU mismatch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldDispatchUpdatedEvent_WhenBarcodeDoesNotMatch()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "OLD-BAR");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSut().Handle(product);

        _eventDispatcher.Received(1).Dispatch(
            Arg.Is<ProductChangedEvent>(e => e.ProductVariantId != Guid.Empty && e.ChangeType == ProductChangeType.Updated));
    }

    [Fact]
    public async Task Handle_ShouldDispatchUpdatedEvent_WhenSkuDoesNotMatch()
    {
        SeedVariant(100, 200, sku: "OLD-SKU", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        _eventDispatcher.Received(1).Dispatch(
            Arg.Is<ProductChangedEvent>(e => e.ProductVariantId != Guid.Empty && e.ChangeType == ProductChangeType.Updated));
    }

    [Fact]
    public async Task Handle_ShouldNotDispatchUpdatedEvent_WhenBarcodeIsEmptyInDatabase()
    {
        // Display name must match so UpdateEntity returns false; only DidBarcodeOrSkuChange is tested.
        SeedVariant(100, 200, displayName: "T-Shirt - Large", sku: "SKU-A", barcode: "");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSut().Handle(product);

        _eventDispatcher.DidNotReceive().Dispatch(Arg.Any<ProductChangedEvent>());
    }

    [Fact]
    public async Task Handle_ShouldNotDispatchUpdatedEvent_WhenSkuIsEmptyInDatabase()
    {
        // Display name must match so UpdateEntity returns false; only DidBarcodeOrSkuChange is tested.
        SeedVariant(100, 200, displayName: "T-Shirt - Large", sku: "", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        _eventDispatcher.DidNotReceive().Dispatch(Arg.Any<ProductChangedEvent>());
    }

    [Fact]
    public async Task Handle_ShouldNotDispatchAnyEvent_WhenVariantIsFullyUpToDate()
    {
        SeedVariant(100, 200, displayName: "T-Shirt - Large", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        _eventDispatcher.DidNotReceive().Dispatch(Arg.Any<ProductChangedEvent>());
    }

    // -------------------------------------------------------------------------
    // Mixed scenarios
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCreateAndUpdateVariants_InSameCall()
    {
        SeedVariant(100, 200, displayName: "T-Shirt", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "SKU-A", barcode: "BAR-A"),  // existing
            CreateVariant(201, "T-Shirt - Small", sku: "SKU-B", barcode: "BAR-B")); // new

        await CreateSut().Handle(product);

        var variants = await _dbContext.ShopifyProductVariants.ToListAsync();
        variants.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_ShouldDispatchOneEventPerVariant_WhenMixedCreatedAndUpdated()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, "T-Shirt - Large", sku: "SKU-A", barcode: "NEW-BAR"), // existing → Updated (barcode mismatch)
            CreateVariant(201, "T-Shirt - Small", sku: "SKU-B", barcode: "BAR-B")); // new → Created

        await CreateSut().Handle(product);

        _eventDispatcher.Received(1).Dispatch(
            Arg.Is<ProductChangedEvent>(e => e.ChangeType == ProductChangeType.Updated));
        _eventDispatcher.Received(1).Dispatch(
            Arg.Is<ProductChangedEvent>(e => e.ChangeType == ProductChangeType.Created));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ShopifyProductUpdateWebhookHandler CreateSut() =>
        new(_dbContext, _logger, _eventDispatcher);

    private void SeedVariant(
        long productId,
        long variantId,
        string displayName = "T-Shirt",
        string sku = "SKU",
        string barcode = "BAR")
    {
        _dbContext.ShopifyProductVariants.Add(new ShopifyProductVariantEntity
        {
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = displayName,
            Sku = sku,
            Barcode = barcode
        });
    }

    private static SqsShopEventProduct CreateProduct(long id, params SqsShopEventVariant[] variants) =>
        new($"gid://shopify/Product/{id}", id, variants);

    private static SqsShopEventVariant CreateVariant(long id, string displayName, string sku, string barcode) =>
        new($"gid://shopify/ProductVariant/{id}", barcode, id, ProductId: 100, sku, displayName);

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
