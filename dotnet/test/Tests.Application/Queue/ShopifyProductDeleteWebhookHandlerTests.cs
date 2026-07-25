using Application;
using Application.Products.Webhook;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using NSubstitute;
using Shouldly;

namespace Tests.Application.Queue;

public class ShopifyProductDeleteWebhookHandlerTests : IDisposable
{
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ShopifyProductDeleteWebhookHandler> _logger = new();

    public ShopifyProductDeleteWebhookHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Default to enabled for behavioural tests. Override per-test if needed.
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifySyncEnabled).Returns(true);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Handle_ShouldMarkAllVariantsDeleted_ForTheProduct()
    {
        SeedVariant(100, 200);
        SeedVariant(100, 201);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Handle(CreateProduct(100));

        var variants = await _dbContext.ShopifyProductVariants.ToListAsync();
        variants.Count.ShouldBe(2);
        variants.ShouldAllBe(v => v.IsDeleted);
        variants.ShouldAllBe(v => v.DeletedOn > DateTime.MinValue);
    }

    [Fact]
    public async Task Handle_ShouldLeaveIsActiveUntouched_WhenMarkingDeleted()
    {
        SeedVariant(100, 200, isActive: true);
        SeedVariant(100, 201, isActive: false);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Handle(CreateProduct(100));

        var active = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.VariantId == 200);
        var inactive = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.VariantId == 201);
        active.IsActive.ShouldBeTrue();
        inactive.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_ShouldNotTouchVariantsOfOtherProducts()
    {
        SeedVariant(100, 200);
        SeedVariant(999, 300);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Handle(CreateProduct(100));

        var other = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.ProductId == 999);
        other.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_ShouldWriteDeletionLogEvent_PerVariant()
    {
        var variant = SeedVariant(100, 200);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Handle(CreateProduct(100));

        var logMessages = await _dbContext.ShopifyProductVariantLogEvents
            .Where(e => e.ShopifyProductVariantId == variant.ShopifyProductVariantId)
            .Select(e => e.Message)
            .ToListAsync();
        logMessages.ShouldContain(m => m.Contains("deleted"));
    }

    [Fact]
    public async Task Handle_ShouldBeIdempotent_WhenVariantAlreadyDeleted()
    {
        var variant = SeedVariant(100, 200, isDeleted: true);
        variant.DeletedOn = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Handle(CreateProduct(100));

        // The original deletion timestamp is not overwritten and no new audit event is written.
        var reloaded = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.VariantId == 200);
        reloaded.DeletedOn.ShouldBe(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var logCount = await _dbContext.ShopifyProductVariantLogEvents
            .CountAsync(e => e.ShopifyProductVariantId == variant.ShopifyProductVariantId);
        logCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ShouldDoNothing_WhenShopifySyncFeatureFlagIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifySyncEnabled).Returns(false);
        SeedVariant(100, 200);
        await _dbContext.SaveChangesAsync();

        await CreateSut().Handle(CreateProduct(100));

        var variant = await _dbContext.ShopifyProductVariants.SingleAsync();
        variant.IsDeleted.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ShopifyProductDeleteWebhookHandler CreateSut() =>
        new(_dbContext, _logger, _featureManager);

    private ShopifyProductVariantEntity SeedVariant(
        long productId,
        long variantId,
        bool isActive = true,
        bool isDeleted = false)
    {
        var entity = new ShopifyProductVariantEntity
        {
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = "T-Shirt (Large)",
            Sku = "SKU",
            Barcode = "BAR",
            IsActive = isActive,
            IsDeleted = isDeleted
        };
        _dbContext.ShopifyProductVariants.Add(entity);
        return entity;
    }

    private static SqsShopEventProduct CreateProduct(long id) =>
        new($"gid://shopify/Product/{id}", id, "T-Shirt", []);

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
