using Application.Products.Events;
using Application.Products.Webhook;
using Infrastructure.Database;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using SlimMessageBus;

namespace Tests.Application.Queue;

public class ShopifyProductCreateWebhookHandlerTests : IDisposable
{
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ShopifyProductUpdateWebhookHandler> _logger = new();

    public ShopifyProductCreateWebhookHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    // -------------------------------------------------------------------------
    // Entity persistence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldPersistOneEntity_PerVariant()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100,
            CreateVariant("gid://shopify/ProductVariant/200", 200, "T-Shirt - Large"),
            CreateVariant("gid://shopify/ProductVariant/201", 201, "T-Shirt - Small"));

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_ShouldSetAllEntityFields_FromProductAndVariant()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100,
            CreateVariant("gid://shopify/ProductVariant/200", 200, "T-Shirt - Large"));

        await CreateSut().Handle(product);

        var entity = await _dbContext.ShopifyProductVariants.SingleAsync();
        entity.GlobalProductId.ShouldBe("gid://shopify/Product/100");
        entity.ProductId.ShouldBe(100L);
        entity.GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/200");
        entity.VariantId.ShouldBe(200L);
    }

    [Fact]
    public async Task Handle_ShouldUseVariantId_AsInitialSkuAndBarcode()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100,
            CreateVariant("gid://shopify/ProductVariant/200", 200, "T-Shirt - Large"));

        await CreateSut().Handle(product);

        var entity = await _dbContext.ShopifyProductVariants.SingleAsync();
        entity.Sku.ShouldBe("200");
        entity.Barcode.ShouldBe("200");
    }

    [Fact]
    public async Task Handle_ShouldPersistNoEntities_WhenProductHasNoVariants()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100);

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Event publishing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldPublishCreatedEvent_PerPersistedVariant()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100,
            CreateVariant("gid://shopify/ProductVariant/200", 200, "T-Shirt - Large"),
            CreateVariant("gid://shopify/ProductVariant/201", 201, "T-Shirt - Small"));

        await CreateSut().Handle(product);

        await _messageBus.Received().Publish(
            Arg.Is<IEnumerable<ProductVariantCreatedEvent>>(events =>
                events.Count() == 2 && events.All(e => e.ProductVariantId != Guid.Empty)),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotPublishAnyEvent_WhenProductHasNoVariants()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100);

        await CreateSut().Handle(product);

        await _messageBus.DidNotReceive().Publish(
            Arg.Is<IEnumerable<ProductVariantCreatedEvent>>(events => events.Any()),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ShopifyProductCreateWebhookHandler CreateSut() =>
        new(_dbContext, _logger, _messageBus);

    private static SqsShopEventProduct CreateProduct(
        string adminGraphqlApiId, long id, params SqsShopEventVariant[] variants) =>
        new(adminGraphqlApiId, id, variants);

    private static SqsShopEventVariant CreateVariant(string adminGraphqlApiId, long id, string displayName) =>
        new(adminGraphqlApiId, Barcode: id.ToString(), id, ProductId: 100, Sku: id.ToString(), displayName);

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
