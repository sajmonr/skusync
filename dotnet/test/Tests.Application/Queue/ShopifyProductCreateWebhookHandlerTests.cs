using Application.Events;
using Application.Products.Events;
using Application.Products.Webhook;
using Infrastructure.Database;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Tests.Application.Queue;

public class ShopifyProductCreateWebhookHandlerTests : IDisposable
{
    private readonly IEventDispatcher _eventDispatcher = Substitute.For<IEventDispatcher>();
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
        var product = CreateProduct("gid://shopify/Product/100", 100, "T-Shirt",
            CreateVariant("gid://shopify/ProductVariant/200", 200, "Large"),
            CreateVariant("gid://shopify/ProductVariant/201", 201, "Small"));

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_ShouldSetAllEntityFields_FromProductAndVariant()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100, "T-Shirt",
            CreateVariant("gid://shopify/ProductVariant/200", 200, "Large"));

        await CreateSut().Handle(product);

        var entity = await _dbContext.ShopifyProductVariants.SingleAsync();
        entity.GlobalProductId.ShouldBe("gid://shopify/Product/100");
        entity.ProductId.ShouldBe(100L);
        entity.GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/200");
        entity.VariantId.ShouldBe(200L);
        entity.ProductTitle.ShouldBe("T-Shirt");
        entity.VariantTitle.ShouldBe("Large");
    }

    [Fact]
    public async Task Handle_ShouldUseVariantId_AsInitialSkuAndBarcode()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100, "T-Shirt",
            CreateVariant("gid://shopify/ProductVariant/200", 200, "Large"));

        await CreateSut().Handle(product);

        var entity = await _dbContext.ShopifyProductVariants.SingleAsync();
        entity.Sku.ShouldBe("200");
        entity.Barcode.ShouldBe("200");
    }

    [Fact]
    public async Task Handle_ShouldPersistNoEntities_WhenProductHasNoVariants()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100, "T-Shirt");

        await CreateSut().Handle(product);

        var saved = await _dbContext.ShopifyProductVariants.ToListAsync();
        saved.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Event dispatching
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ShouldDispatchCreatedEvent_PerPersistedVariant()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100, "T-Shirt",
            CreateVariant("gid://shopify/ProductVariant/200", 200, "Large"),
            CreateVariant("gid://shopify/ProductVariant/201", 201, "Small"));

        await CreateSut().Handle(product);

        _eventDispatcher.Received(2).Dispatch(
            Arg.Is<ProductChangedEvent>(e => e.ProductVariantId != Guid.Empty && e.ChangeType == ProductChangeType.Created));
    }

    [Fact]
    public async Task Handle_ShouldNotDispatchAnyEvent_WhenProductHasNoVariants()
    {
        var product = CreateProduct("gid://shopify/Product/100", 100, "T-Shirt");

        await CreateSut().Handle(product);

        _eventDispatcher.DidNotReceive().Dispatch(Arg.Any<ProductChangedEvent>());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ShopifyProductCreateWebhookHandler CreateSut() =>
        new(_dbContext, _logger, _eventDispatcher);

    private static SqsShopEventProduct CreateProduct(
        string adminGraphqlApiId, long id, string title, params SqsShopEventVariant[] variants) =>
        new(adminGraphqlApiId, id, title, variants);

    private static SqsShopEventVariant CreateVariant(string adminGraphqlApiId, long id, string title) =>
        new(adminGraphqlApiId, Barcode: id.ToString(), id, ProductId: 100, Sku: id.ToString(), title);

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
