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
    private readonly IShopifyProductService _productService = Substitute.For<IShopifyProductService>();
    private readonly IEventAccumulator<ProductChangedEvent> _eventAccumulator = Substitute.For<IEventAccumulator<ProductChangedEvent>>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ShopifyProductUpdateWebhookHandler> _logger = new();

    public ShopifyProductUpdateWebhookHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        _productService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);
    }

    public void Dispose() => _dbContext.Dispose();

    // -------------------------------------------------------------------------
    // New variant creation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCreateEntity_WhenVariantDoesNotExistInDatabase()
    {
        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.Count.ShouldBe(1);
        saved[0].VariantId.ShouldBe(200L);
    }

    [Fact]
    public async Task Handle_ShouldCallUpdateVariants_WhenNewVariantIsCreated()
    {
        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await _productService.Received(1).UpdateVariants(
            "gid://shopify/Product/100",
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(v => v.Any()));
    }

    // -------------------------------------------------------------------------
    // Title updates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldUpdateProductTitle_WhenTitleChangedInShopify()
    {
        SeedVariant(100, 200, productTitle: "Old Title", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "New Title",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.ProductTitle.ShouldBe("New Title");
    }

    [Fact]
    public async Task Handle_ShouldUpdateVariantTitle_WhenTitleChangedInShopify()
    {
        SeedVariant(100, 200, variantTitle: "Small", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.VariantTitle.ShouldBe("Large");
    }

    [Fact]
    public async Task Handle_ShouldNotUpdateVariantTitle_WhenIncomingTitleIsDefaultTitle()
    {
        SeedVariant(100, 200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Default Title", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.VariantTitle.ShouldBe("Large");
    }

    // -------------------------------------------------------------------------
    // Shopify sync — barcode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCallUpdateVariants_WhenBarcodeDoesNotMatch()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "OLD-BAR");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSut().Handle(product);

        await _productService.Received(1).UpdateVariants(
            "gid://shopify/Product/100",
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(v => v.Any()));
    }

    // -------------------------------------------------------------------------
    // Shopify sync — SKU (exercises the bug fix)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCallUpdateVariants_WhenSkuDoesNotMatch()
    {
        SeedVariant(100, 200, sku: "OLD-SKU", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await _productService.Received(1).UpdateVariants(
            "gid://shopify/Product/100",
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(v => v.Any()));
    }

    [Fact]
    public async Task Handle_ShouldNotCallUpdateVariants_WhenBarcodeIsEmptyInDatabase()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSut().Handle(product);

        await _productService.Received(1).UpdateVariants(
            Arg.Any<string>(),
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(v => !v.Any()));
    }

    [Fact]
    public async Task Handle_ShouldNotCallUpdateVariants_WhenSkuIsEmptyInDatabase()
    {
        SeedVariant(100, 200, sku: "", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await _productService.Received(1).UpdateVariants(
            Arg.Any<string>(),
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(v => !v.Any()));
    }

    // -------------------------------------------------------------------------
    // No Shopify sync needed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldNotCallUpdateVariants_WhenVariantIsFullyUpToDate()
    {
        SeedVariant(100, 200, productTitle: "T-Shirt", variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await _productService.Received(1).UpdateVariants(
            Arg.Any<string>(),
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(v => !v.Any()));
    }

    // -------------------------------------------------------------------------
    // Mixed scenarios
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCreateAndUpdateVariants_InSameCall()
    {
        SeedVariant(100, 200, productTitle: "T-Shirt", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"),  // existing
            CreateVariant(201, "Small", sku: "SKU-B", barcode: "BAR-B")); // new

        await CreateSut().Handle(product);

        var variants = await _dbContext.ShopifyProductVariants.ToListAsync();
        variants.Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Event accumulation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldEnqueueCreatedEvent_WhenNewVariantIsSaved()
    {
        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        _eventAccumulator.Received(1).Enqueue(
            Arg.Is<ProductChangedEvent>(e => e.VariantId == 200L && e.ChangeType == ProductChangeType.Created));
    }

    [Fact]
    public async Task Handle_ShouldEnqueueUpdatedEvent_WhenExistingVariantIsProcessed()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        _eventAccumulator.Received(1).Enqueue(
            Arg.Is<ProductChangedEvent>(e => e.VariantId == 200L && e.ChangeType == ProductChangeType.Updated));
    }

    [Fact]
    public async Task Handle_ShouldEnqueueOneEventPerVariant_WhenMultipleVariantsArePresent()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, "T-Shirt",
            CreateVariant(200, "Large", sku: "SKU-A", barcode: "BAR-A"),  // existing → Updated
            CreateVariant(201, "Small", sku: "SKU-B", barcode: "BAR-B")); // new → Created

        await CreateSut().Handle(product);

        _eventAccumulator.Received(1).Enqueue(
            Arg.Is<ProductChangedEvent>(e => e.VariantId == 200L && e.ChangeType == ProductChangeType.Updated));
        _eventAccumulator.Received(1).Enqueue(
            Arg.Is<ProductChangedEvent>(e => e.VariantId == 201L && e.ChangeType == ProductChangeType.Created));
    }

    private ShopifyProductUpdateWebhookHandler CreateSut() =>
        new(_dbContext, _productService, _logger, _eventAccumulator);

    private void SeedVariant(
        long productId,
        long variantId,
        string productTitle = "T-Shirt",
        string variantTitle = "",
        string sku = "SKU",
        string barcode = "BAR")
    {
        _dbContext.ShopifyProductVariants.Add(new ShopifyProductVariantEntity
        {
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            ProductTitle = productTitle,
            VariantTitle = variantTitle,
            Sku = sku,
            Barcode = barcode
        });
    }

    private static SqsShopEventProduct CreateProduct(long id, string title, params SqsShopEventVariant[] variants) =>
        new($"gid://shopify/Product/{id}", id, title, variants);

    private static SqsShopEventVariant CreateVariant(long id, string title, string sku, string barcode) =>
        new($"gid://shopify/ProductVariant/{id}", barcode, id, ProductId: 100, sku, title);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
