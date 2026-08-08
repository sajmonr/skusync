using Application;
using Application.Products.Webhook;
using Application.Skus;
using Application.Sync;
using Application.Sync.Merge;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using NSubstitute;
using Shouldly;
using Tests.Application.Sync;

namespace Tests.Application.Queue;

public class ShopifyProductUpdateWebhookHandlerTests : IDisposable
{
    private readonly IReconciler _reconciler = Substitute.For<IReconciler>();
    private readonly IShopifyDispatchTrigger _dispatchTrigger = Substitute.For<IShopifyDispatchTrigger>();
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
                Arg.Any<ISet<string>?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult($"GEN-{Guid.NewGuid():N}"[..12]));

        _reconciler.ReconcileVariants(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<MergeOrigin>(), Arg.Any<CancellationToken>())
            .Returns(ReconcileResult.Empty);
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
    public async Task Handle_ShouldMarkPendingAndDispatchNewVariant_WhenNewVariantIsSaved()
    {
        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSutWithRealGenerator().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.SingleAsync();
        saved.PendingShopifySync.ShouldBeTrue();
        await _dispatchTrigger.Received(1).TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(saved.ShopifyProductVariantId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReconcileNewVariant_BeforeDispatching()
    {
        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.SingleAsync();
        await _reconciler.Received(1).ReconcileVariants(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(saved.ShopifyProductVariantId)),
            MergeOrigin.WebhookCreate,
            Arg.Any<CancellationToken>());
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
    // Pending marking and dispatch — barcode / SKU mismatch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldMarkPendingAndDispatch_WhenBarcodeDoesNotMatch()
    {
        var seeded = SeedVariant(100, 200, sku: "SKU-A", barcode: "OLD-BAR");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSutWithRealGenerator().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.PendingShopifySync.ShouldBeTrue();
        await _dispatchTrigger.Received(1).TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(seeded.ShopifyProductVariantId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldMarkPendingAndDispatch_WhenSkuDoesNotMatch()
    {
        var seeded = SeedVariant(100, 200, sku: "OLD-SKU", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSutWithRealGenerator().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.PendingShopifySync.ShouldBeTrue();
        await _dispatchTrigger.Received(1).TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(seeded.ShopifyProductVariantId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotMarkPending_WhenBarcodeIsEmptyInDatabase()
    {
        // Display name must match so UpdateEntity returns false; only DidBarcodeOrSkuChange is tested.
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "SKU-A", barcode: "");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "NEW-BAR"));

        await CreateSutWithRealGenerator().Handle(product);

        var variant = await _dbContext.ShopifyProductVariants.SingleAsync();
        variant.PendingShopifySync.ShouldBeFalse();
        await AssertNoVariantsDispatched();
    }

    [Fact]
    public async Task Handle_ShouldNotMarkPending_WhenSkuIsEmptyInDatabase()
    {
        // Display name must match so UpdateEntity returns false; only DidBarcodeOrSkuChange is tested.
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "NEW-SKU", barcode: "BAR-A"));

        await CreateSutWithRealGenerator().Handle(product);

        var variant = await _dbContext.ShopifyProductVariants.SingleAsync();
        variant.PendingShopifySync.ShouldBeFalse();
        await AssertNoVariantsDispatched();
    }

    [Fact]
    public async Task Handle_ShouldNotMarkPendingOrDispatch_WhenVariantIsFullyUpToDate()
    {
        SeedVariant(100, 200, displayName: "T-Shirt (Large)", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSut().Handle(product);

        var variant = await _dbContext.ShopifyProductVariants.SingleAsync();
        variant.PendingShopifySync.ShouldBeFalse();
        await AssertNoVariantsDispatched();
    }

    /// <summary>
    /// A rename changes what SkuLabs is owed, not what Shopify is: Shopify is where the new title
    /// came from. Nothing should be queued back at it.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldNotDispatch_WhenOnlyTheDisplayNameChanged()
    {
        var seeded = SeedVariant(100, 200, displayName: "Old Product (Old Variant)", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100, productTitle: "New Product",
            CreateVariant(200, variantTitle: "New Variant", sku: "SKU-A", barcode: "BAR-A"));

        await CreateSutWithRealGenerator().Handle(product);

        var updated = await _dbContext.ShopifyProductVariants.SingleAsync();
        updated.DisplayName.ShouldBe("New Product (New Variant)");
        updated.PendingShopifySync.ShouldBeFalse();
        await AssertNoVariantsDispatched();
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

    /// <summary>
    /// The two groups cannot share a reconcile call: a first sighting has its payload codes
    /// replaced, an existing variant keeps what was already decided for it.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldReconcileBothGroupsSeparately_WhenMixedCreatedAndUpdated()
    {
        var seeded = SeedVariant(100, 200, sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        var product = CreateProduct(100,
            CreateVariant(200, variantTitle: "Large", sku: "SKU-A", barcode: "NEW-BAR"), // existing → updated (barcode mismatch)
            CreateVariant(201, variantTitle: "Small", sku: "SKU-B", barcode: "BAR-B")); // new → created

        await CreateSut().Handle(product);

        var created = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.VariantId == 201);
        await _reconciler.Received(1).ReconcileVariants(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(created.ShopifyProductVariantId)),
            MergeOrigin.WebhookCreate,
            Arg.Any<CancellationToken>());
        await _reconciler.Received(1).ReconcileVariants(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(seeded.ShopifyProductVariantId)),
            MergeOrigin.Routine,
            Arg.Any<CancellationToken>());
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
        await AssertNoVariantsDispatched();
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
        await _dispatchTrigger.DidNotReceive().TryDispatch(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _reconciler.DidNotReceive().ReconcileVariants(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count > 0),
            Arg.Any<MergeOrigin>(),
            Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Unabbreviatable product titles (regression: issue #38 poison message)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldNotThrow_AndAssignFallbackSku_WhenNewVariantTitleIsUnabbreviatable()
    {
        // An emoji-only title strips to an empty abbreviation. Before #38 the SKU generator
        // threw when creating the new variant, and the exception propagated to the SQS handler,
        // turning this webhook into a poison message that retried forever. It must now degrade to
        // a variant-id-derived SKU. Uses the real generator so the throw path is exercised.
        var product = CreateProduct(100, productTitle: "🎁",
            CreateVariant(200, variantTitle: "Small / Black", sku: "", barcode: "BAR-A"));

        await CreateSutWithRealGenerator().Handle(product);

        var entity = await _dbContext.ShopifyProductVariants.SingleAsync();
        (await DesiredFor(200)).Sku.ShouldBe("BW-200-SM-BL");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------


    /// <summary>The decided state for a variant — where SKUs and barcodes now live.</summary>
    private async Task<DesiredItemStateEntity> DesiredFor(long variantId) =>
        await _dbContext.DesiredItemStates
            .SingleAsync(state => state.ShopifyProductVariant!.VariantId == variantId);

    private ShopifyProductUpdateWebhookHandler CreateSut() =>
        new(_dbContext, _logger, _reconciler, _dispatchTrigger, _featureManager);

    /// <summary>SUT wired to the real reconciler, for tests asserting the resulting values.</summary>
    private ShopifyProductUpdateWebhookHandler CreateSutWithRealGenerator() =>
        new(_dbContext, _logger,
            MergeTestFactory.CreateReconciler(_dbContext),
            _dispatchTrigger, _featureManager);

    // The handler always calls TryDispatch after a save — with an empty id set when nothing was
    // touched — so "nothing dispatched" means no call carrying at least one variant id.
    private async Task AssertNoVariantsDispatched()
    {
        await _dispatchTrigger.DidNotReceive().TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count > 0),
            Arg.Any<CancellationToken>());
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

        // Post-migration every variant has one, seeded from its own values. Without it there is
        // nothing recording that these codes were ever decided, and Shopify drifting away from them
        // would read as Shopify simply being right.
        _dbContext.DesiredItemStates.Add(new DesiredItemStateEntity
        {
            DesiredItemStateId = Guid.NewGuid(),
            ShopifyProductVariantId = entity.ShopifyProductVariantId,
            Sku = sku,
            Barcode = barcode,
            Title = displayName
        });

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
