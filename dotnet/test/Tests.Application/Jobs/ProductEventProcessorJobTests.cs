using Application;
using Application.Events;
using Application.Products.Events;
using Application.Products.Jobs;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using NSubstitute;
using Quartz;
using Shouldly;

namespace Tests.Application.Jobs;

public class ProductEventProcessorJobTests : IDisposable
{
    private readonly IEventAccumulator<ProductChangedEvent> _eventAccumulator =
        Substitute.For<IEventAccumulator<ProductChangedEvent>>();
    private readonly IEventDispatcher _eventDispatcher = Substitute.For<IEventDispatcher>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly ApplicationDbContext _dbContext;
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly ILogger<ProductEventProcessorJob> _logger =
        Substitute.For<ILogger<ProductEventProcessorJob>>();

    public ProductEventProcessorJobTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        // Default: no accumulated events.
        _eventAccumulator.DrainAll().Returns([]);
        // Default: feature flag enabled.
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(true);
        // Default: Shopify update succeeds.
        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);
    }

    public void Dispose() => _dbContext.Dispose();

    // -------------------------------------------------------------------------
    // Accumulator interaction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldDrainAccumulator_OnEveryRun()
    {
        await CreateSut().Execute(_context);

        _eventAccumulator.Received(1).DrainAll();
    }

    [Fact]
    public async Task Execute_ShouldNotCallShopify_WhenNoEventsAccumulated()
    {
        _eventAccumulator.DrainAll().Returns([]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    // -------------------------------------------------------------------------
    // Feature flag: ShopifyWriteBack disabled
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldNotCallShopify_WhenShopifyWriteBackFlagIsDisabled_ForCreatedEvents()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(false);
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Created(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldNotCallShopify_WhenShopifyWriteBackFlagIsDisabled_ForUpdatedEvents()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(false);
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Updated(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    // -------------------------------------------------------------------------
    // Created events
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldCallShopify_WhenCreatedEventsArePresent()
    {
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Created(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1)
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldCallShopifyOncePerProduct_WhenCreatedVariantsBelongToSameProduct()
    {
        var entity1 = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        var entity2 = SeedVariant("gid://shopify/ProductVariant/200", "gid://shopify/Product/10");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns(
        [
            ProductChangedEvent.Created(entity1.ShopifyProductVariantId),
            ProductChangedEvent.Created(entity2.ShopifyProductVariantId)
        ]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1)
            .UpdateVariants("gid://shopify/Product/10", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldCallShopifyOncePerProduct_WhenCreatedVariantsBelongToMultipleProducts()
    {
        var entity1 = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        var entity2 = SeedVariant("gid://shopify/ProductVariant/200", "gid://shopify/Product/20");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns(
        [
            ProductChangedEvent.Created(entity1.ShopifyProductVariantId),
            ProductChangedEvent.Created(entity2.ShopifyProductVariantId)
        ]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1)
            .UpdateVariants("gid://shopify/Product/10", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
        await _shopifyProductService.Received(1)
            .UpdateVariants("gid://shopify/Product/20", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldPassCorrectVariantData_WhenCallingShopifyForCreatedEvents()
    {
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10",
            sku: "MY-SKU", barcode: "MY-BAR");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Created(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1).UpdateVariants(
            "gid://shopify/Product/10",
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(variants =>
                variants.Any(v =>
                    v.GlobalVariantId == "gid://shopify/ProductVariant/100" &&
                    v.Sku == "MY-SKU" &&
                    v.Barcode == "MY-BAR")));
    }

    [Fact]
    public async Task Execute_ShouldDispatchSkulabsImportEvent_WhenCreatedEventsAreProcessed()
    {
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Created(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        _eventDispatcher.Received(1).Dispatch(Arg.Any<SkulabsProductImportEvent>());
    }

    [Fact]
    public async Task Execute_ShouldNotDispatchSkulabsImportEvent_WhenOnlyUpdatedEventsAreProcessed()
    {
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Updated(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        _eventDispatcher.DidNotReceive().Dispatch(Arg.Any<SkulabsProductImportEvent>());
    }

    // -------------------------------------------------------------------------
    // Updated events
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldCallShopify_WhenUpdatedEventsArePresent()
    {
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Updated(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1)
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldCallShopifyOncePerProduct_WhenUpdatedVariantsBelongToMultipleProducts()
    {
        var entity1 = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10");
        var entity2 = SeedVariant("gid://shopify/ProductVariant/200", "gid://shopify/Product/20");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns(
        [
            ProductChangedEvent.Updated(entity1.ShopifyProductVariantId),
            ProductChangedEvent.Updated(entity2.ShopifyProductVariantId)
        ]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1)
            .UpdateVariants("gid://shopify/Product/10", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
        await _shopifyProductService.Received(1)
            .UpdateVariants("gid://shopify/Product/20", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task Execute_ShouldPassCorrectVariantData_WhenCallingShopifyForUpdatedEvents()
    {
        var entity = SeedVariant("gid://shopify/ProductVariant/100", "gid://shopify/Product/10",
            sku: "MY-SKU", barcode: "MY-BAR");
        await _dbContext.SaveChangesAsync();
        _eventAccumulator.DrainAll().Returns([ProductChangedEvent.Updated(entity.ShopifyProductVariantId)]);

        await CreateSut().Execute(_context);

        await _shopifyProductService.Received(1).UpdateVariants(
            "gid://shopify/Product/10",
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(variants =>
                variants.Any(v =>
                    v.GlobalVariantId == "gid://shopify/ProductVariant/100" &&
                    v.Sku == "MY-SKU" &&
                    v.Barcode == "MY-BAR")));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ShopifyProductVariantEntity SeedVariant(
        string globalVariantId,
        string globalProductId,
        string sku = "SKU",
        string barcode = "BAR")
    {
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = globalProductId,
            ProductId = long.Parse(globalProductId.Split('/').Last()),
            GlobalVariantId = globalVariantId,
            VariantId = long.Parse(globalVariantId.Split('/').Last()),
            ProductTitle = "Product",
            VariantTitle = "",
            FullTitle = "Product",
            Sku = sku,
            Barcode = barcode
        };
        _dbContext.Set<ShopifyProductVariantEntity>().Add(entity);
        return entity;
    }

    private ProductEventProcessorJob CreateSut() =>
        new(_eventAccumulator, _eventDispatcher, _logger, _featureManager, _shopifyProductService, _dbContext);
}
