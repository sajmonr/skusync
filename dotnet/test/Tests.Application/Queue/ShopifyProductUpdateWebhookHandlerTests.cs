using Application;
using Application.Products.Events;
using Application.Products.Webhook;
using Application.Skus;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using NSubstitute;
using Shouldly;
using SlimMessageBus;

namespace Tests.Application.Queue;

public class ShopifyProductUpdateWebhookHandlerTests : IDisposable
{
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly ISkuGenerator _skuGenerator = Substitute.For<ISkuGenerator>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ShopifyProductUpdateWebhookHandler> _logger = new();

    public ShopifyProductUpdateWebhookHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Default to enabled for existing behavioural tests. Override per-test if needed.
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifySyncEnabled).Returns(true);

        _skuGenerator.Generate(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<ISet<string>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult($"GEN-{Guid.NewGuid():N}"[..12]));
    }

    public void Dispose() => _dbContext.Dispose();

    // -------------------------------------------------------------------------
    // New variant creation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCreateEntity_WhenVariantDoesNotExistInDatabase()
    {
        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.Count.ShouldBe(1);
        saved[0].VariantId.ShouldBe(200L);
    }

    [Fact]
    public async Task Handle_ShouldPublishCreatedEvent_WhenNewVariantIsSaved()
    {
        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await _messageBus.Received(1).Publish(
            Arg.Is<ProductVariantCreatedEvent>(e => e.ProductVariantId != Guid.Empty),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Deactivated variants — must update, not re-insert
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldUpdateNotInsert_WhenMatchingVariantIsInactive()
    {
        SeedVariant(100, 200, displayName: "Old Product (Old Variant)", sku: "SKU-A", barcode: "BAR-A",
            isActive: false, failedShopifySyncAttempts: 3);
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "New Product",
            CreateVariant(200, variantTitle: "New Variant", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.Count.ShouldBe(1);
        saved[0].DisplayName.ShouldBe("New Product (New Variant)");
    }

    [Fact]
    public async Task Handle_ShouldReactivateAndResetFailures_WhenInactiveVariantReceivesWebhook()
    {
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "SKU-A", barcode: "BAR-A",
            isActive: false, failedShopifySyncAttempts: 3);
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var revived = await _dbContext.ShopifyProductVariants.SingleAsync();
        revived.IsActive.ShouldBeTrue();
        revived.FailedShopifySyncAttempts.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // Display name updates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldUpdateDisplayName_WhenProductOrVariantTitleChangedInShopify()
    {
        SeedVariant(100, 200, displayName: "Old Product (Old Variant)", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "New Product",
            CreateVariant(200, variantTitle: "New Variant", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.DisplayName.ShouldBe("New Product (New Variant)");
    }

    [Fact]
    public async Task Handle_ShouldUseProductTitleOnly_WhenVariantTitleIsDefaultTitle()
    {
        SeedVariant(100, 200, displayName: "Old Product", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "New Product",
            CreateVariant(200, variantTitle: "Default Title", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.DisplayName.ShouldBe("New Product");
    }

    // -------------------------------------------------------------------------
    // Updated event dispatching — barcode / SKU mismatch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldPublishUpdatedEvent_WhenBarcodeDoesNotMatch()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "OLD-BAR");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSut().Handle(product);

        await _messageBus.Received(1).Publish(
            Arg.Is<ProductVariantUpdatedEvent>(e => e.ProductVariantId != Guid.Empty),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPublishUpdatedEvent_WhenSkuDoesNotMatch()
    {
        SeedVariant(100, 200, sku: "OLD-SKU", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await _messageBus.Received(1).Publish(
            Arg.Is<ProductVariantUpdatedEvent>(e => e.ProductVariantId != Guid.Empty),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotPublishUpdatedEvent_WhenBarcodeIsEmptyInDatabase()
    {
        // Display name must match so UpdateEntity returns false; only DidBarcodeOrSkuChange is tested.
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "SKU-A", barcode: "");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSut().Handle(product);

        await AssertNoEventsPublished();
    }

    [Fact]
    public async Task Handle_ShouldNotPublishUpdatedEvent_WhenSkuIsEmptyInDatabase()
    {
        // Display name must match so UpdateEntity returns false; only DidBarcodeOrSkuChange is tested.
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await AssertNoEventsPublished();
    }

    [Fact]
    public async Task Handle_ShouldNotPublishAnyEvent_WhenVariantIsFullyUpToDate()
    {
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        await AssertNoEventsPublished();
    }

    // -------------------------------------------------------------------------
    // Mixed scenarios
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldCreateAndUpdateVariants_InSameCall()
    {
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"),  // existing
            CreateVariant(201, variantTitle: "Small", sku: "SKU-B", barcode: "BAR-B")); // new

        await CreateSut().Handle(product);

        var variants = await _dbContext.ShopifyProductVariants.ToListAsync();
        variants.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_ShouldPublishOneEventPerVariant_WhenMixedCreatedAndUpdated()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "NEW-BAR"), // existing → Updated (barcode mismatch)
            CreateVariant(201, variantTitle: "Small", sku: "SKU-B", barcode: "BAR-B")); // new → Created

        await CreateSut().Handle(product);

        await _messageBus.Received(1).Publish(
            Arg.Any<ProductVariantUpdatedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
        await _messageBus.Received(1).Publish(
            Arg.Any<ProductVariantCreatedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Removed variants — default-variant replacement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldMarkVariantDeleted_WhenAbsentFromPayload()
    {
        // The standalone default variant Shopify drops once real variants are created.
        var defaultVariant = SeedVariant(100, 200, displayName: "Necklace", sku: "SKU-DEFAULT", barcode: "");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "Necklace",
            CreateVariant(201, variantTitle: "Small", sku: "SKU-S", barcode: "BAR-S"),
            CreateVariant(202, variantTitle: "Large", sku: "SKU-L", barcode: "BAR-L"));

        await CreateSut().Handle(product);

        var deleted = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.VariantId == 200);
        deleted.IsDeleted.ShouldBeTrue();
        deleted.DeletedOn.ShouldBeGreaterThan(DateTime.MinValue);

        var liveVariants = await _dbContext.ShopifyProductVariants
            .Where(v => v.VariantId != 200)
            .ToListAsync();
        liveVariants.Count.ShouldBe(2);
        liveVariants.ShouldAllBe(v => !v.IsDeleted);
    }

    [Fact]
    public async Task Handle_ShouldWriteDeletionLogEvent_WhenMarkingDeleted()
    {
        var defaultVariant = SeedVariant(100, 200, displayName: "Necklace", sku: "SKU-DEFAULT");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "Necklace",
            CreateVariant(201, variantTitle: "Small", sku: "SKU-S", barcode: "BAR-S"));

        await CreateSut().Handle(product);

        var logMessages = await _dbContext.ShopifyProductVariantLogEvents
            .Where(e => e.ShopifyProductVariantId == defaultVariant.ShopifyProductVariantId)
            .Select(e => e.Message)
            .ToListAsync();
        logMessages.ShouldContain(m => m.Contains("deleted"));
    }

    [Fact]
    public async Task Handle_ShouldNotResurrectOrDuplicate_WhenPayloadReferencesDeletedVariant()
    {
        SeedVariant(100, 200, displayName: "Frozen Name", sku: "SKU-A", barcode: "BAR-A", isDeleted: true);
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "New Product",
            CreateVariant(200, variantTitle: "New Variant", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSut().Handle(product);

        var variants = await _dbContext.ShopifyProductVariants.ToListAsync();
        variants.Count.ShouldBe(1);
        variants[0].IsDeleted.ShouldBeTrue();
        variants[0].DisplayName.ShouldBe("Frozen Name");
        await AssertNoEventsPublished();
    }

    [Fact]
    public async Task Handle_ShouldNotMarkDeleted_WhenPayloadHasNoVariants()
    {
        SeedVariant(100, 200, sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "T-Shirt");

        await CreateSut().Handle(product);

        var variant = await _dbContext.ShopifyProductVariants.SingleAsync();
        variant.IsDeleted.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Feature flag
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldDoNothing_WhenShopifySyncFeatureFlagIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifySyncEnabled).Returns(false);
        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        (await _dbContext.ShopifyProductVariants.CountAsync()).ShouldBe(0);
        await AssertNoEventsPublished();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ShopifyProductUpdateWebhookHandler CreateSut() =>
        new(_dbContext, _logger, _messageBus, _featureManager, _skuGenerator);

    private async Task AssertNoEventsPublished()
    {
        await _messageBus.DidNotReceive().Publish(
            Arg.Any<ProductVariantCreatedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
        await _messageBus.DidNotReceive().Publish(
            Arg.Any<ProductVariantUpdatedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    private ShopifyProductVariantEntity SeedVariant(
        long productId,
        long variantId,
        string displayName = "T-Shirt (Large)",
        string sku = "SKU",
        string barcode = "BAR",
        bool isActive = true,
        int failedShopifySyncAttempts = 0,
        bool isDeleted = false)
    {
        var entity = new ShopifyProductVariantEntity
        {
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = displayName,
            Sku = sku,
            Barcode = barcode,
            IsActive = isActive,
            FailedShopifySyncAttempts = failedShopifySyncAttempts,
            IsDeleted = isDeleted
        };
        _dbContext.ShopifyProductVariants.Add(entity);
        return entity;
    }

    private static SqsShopEventProduct CreateProduct(long id, params SqsShopEventVariant[] variants) =>
        CreateProduct(id, productTitle: "T-Shirt", variants);

    private static SqsShopEventProduct CreateProduct(long id, string productTitle, params SqsShopEventVariant[] variants) =>
        new($"gid://shopify/Product/{id}", id, productTitle, variants);

    private static SqsShopEventVariant CreateVariant(long id, string variantTitle, string sku, string barcode) =>
        new($"gid://shopify/ProductVariant/{id}", barcode, id, ProductId: 100, sku, variantTitle);

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
