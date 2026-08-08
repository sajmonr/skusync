using Application;
using Application.Sync;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Sync;

public class ShopifyDispatcherTests : IDisposable
{
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly ApplicationDbContext _dbContext;

    public ShopifyDispatcherTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        // Default: the kill switch is on (writes allowed) and Shopify accepts updates.
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(true);
        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task DispatchAll_ShouldReturnEmpty_WhenNothingPending()
    {
        SeedVariant(pendingShopifySync: false);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.ShouldBe(DispatchResult.Empty);
        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task DispatchAll_ShouldWritePendingVariant_AndClearFlag()
    {
        var variant = SeedVariant(sku: "new-sku", barcode: "new-bar", pendingShopifySync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.Pending.ShouldBe(1);
        result.Pushed.ShouldBe(1);
        result.Failed.ShouldBe(0);

        await _shopifyProductService.Received(1).UpdateVariants(
            variant.GlobalProductId,
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(updates =>
                updates.Count() == 1
                && updates.First().GlobalVariantId == variant.GlobalVariantId
                && updates.First().Sku == "new-sku"
                && updates.First().Barcode == "new-bar"));

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.PendingShopifySync.ShouldBeFalse();
    }

    [Fact]
    public async Task DispatchAll_ShouldBatchPerProduct()
    {
        SeedVariant(productId: 100, variantId: 1, pendingShopifySync: true);
        var v2 = SeedVariant(productId: 100, variantId: 2, pendingShopifySync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.Pushed.ShouldBe(2);
        await _shopifyProductService.Received(1).UpdateVariants(
            v2.GlobalProductId,
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(updates => updates.Count() == 2));
    }

    [Fact]
    public async Task DispatchAll_ShouldSkipShopify_AndKeepPending_WhenKillSwitchOff()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(false);
        SeedVariant(pendingShopifySync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.Pending.ShouldBe(1);
        result.Pushed.ShouldBe(0);
        result.Failed.ShouldBe(0);
        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());

        (await _dbContext.ShopifyProductVariants.SingleAsync()).PendingShopifySync.ShouldBeTrue();
    }

    [Fact]
    public async Task DispatchAll_ShouldLeavePendingAndIncrementAttempts_WhenShopifyRejects()
    {
        SeedVariant(pendingShopifySync: true);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(false);

        var result = await CreateSut().DispatchAll();

        result.Pushed.ShouldBe(0);
        result.Failed.ShouldBe(1);

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.PendingShopifySync.ShouldBeTrue();
        stored.FailedShopifySyncAttempts.ShouldBe(1);
        stored.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task DispatchAll_ShouldLeavePendingAndIncrementAttempts_WhenShopifyThrows()
    {
        SeedVariant(pendingShopifySync: true);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .ThrowsAsync(new HttpRequestException("shopify offline"));

        var result = await CreateSut().DispatchAll();

        result.Failed.ShouldBe(1);

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.PendingShopifySync.ShouldBeTrue();
        stored.FailedShopifySyncAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchAll_ShouldDeactivateVariant_AfterThreeConsecutiveFailures()
    {
        var variant = SeedVariant(pendingShopifySync: true, failedShopifySyncAttempts: 2);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(false);

        await CreateSut().DispatchAll();

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.FailedShopifySyncAttempts.ShouldBe(3);
        stored.IsActive.ShouldBeFalse();

        var logs = await _dbContext.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variant.ShopifyProductVariantId)
            .ToListAsync();
        logs.ShouldContain(l =>
            l.Message == "Variant deactivated after 3 consecutive failed Shopify sync attempts.");
    }

    [Fact]
    public async Task DispatchAll_ShouldResetFailedAttempts_OnSuccessfulPush()
    {
        SeedVariant(pendingShopifySync: true, failedShopifySyncAttempts: 2);
        await _dbContext.SaveChangesAsync();

        await CreateSut().DispatchAll();

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.FailedShopifySyncAttempts.ShouldBe(0);
        stored.PendingShopifySync.ShouldBeFalse();
    }

    [Fact]
    public async Task DispatchAll_ShouldExcludeInactiveAndDeletedVariants()
    {
        SeedVariant(productId: 1, variantId: 1, pendingShopifySync: true, isActive: false);
        SeedVariant(productId: 2, variantId: 2, pendingShopifySync: true, isDeleted: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.ShouldBe(DispatchResult.Empty);
    }

    [Fact]
    public async Task DispatchAll_ShouldPushGroupB_WhenGroupAThrows()
    {
        var a = SeedVariant(productId: 100, variantId: 1, pendingShopifySync: true);
        var b = SeedVariant(productId: 200, variantId: 2, pendingShopifySync: true);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.UpdateVariants(a.GlobalProductId, Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .ThrowsAsync(new HttpRequestException("shopify offline"));
        _shopifyProductService.UpdateVariants(b.GlobalProductId, Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);

        var result = await CreateSut().DispatchAll();

        result.Pushed.ShouldBe(1);
        result.Failed.ShouldBe(1);

        (await _dbContext.ShopifyProductVariants
            .SingleAsync(v => v.ShopifyProductVariantId == a.ShopifyProductVariantId))
            .PendingShopifySync.ShouldBeTrue();
        (await _dbContext.ShopifyProductVariants
            .SingleAsync(v => v.ShopifyProductVariantId == b.ShopifyProductVariantId))
            .PendingShopifySync.ShouldBeFalse();
    }

    [Fact]
    public async Task DispatchVariants_ShouldOnlyPushTheGivenVariants()
    {
        var target = SeedVariant(productId: 100, variantId: 1, pendingShopifySync: true);
        SeedVariant(productId: 200, variantId: 2, pendingShopifySync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchVariants([target.ShopifyProductVariantId]);

        result.Pushed.ShouldBe(1);
        await _shopifyProductService.Received(1).UpdateVariants(
            target.GlobalProductId, Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
        await _shopifyProductService.DidNotReceive().UpdateVariants(
            "gid://shopify/Product/200", Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task DispatchVariants_ShouldReturnEmpty_ForEmptyScope()
    {
        var result = await CreateSut().DispatchVariants([]);

        result.ShouldBe(DispatchResult.Empty);
    }

    // ---------- Helpers ----------

    private ShopifyDispatcher CreateSut() =>
        new(_dbContext, _shopifyProductService, _featureManager, NullLogger<ShopifyDispatcher>.Instance);

    private ShopifyProductVariantEntity SeedVariant(
        long productId = 100,
        long variantId = 200,
        string sku = "SKU",
        string barcode = "BAR",
        bool pendingShopifySync = false,
        int failedShopifySyncAttempts = 0,
        bool isActive = true,
        bool isDeleted = false,
        string? desiredSku = null,
        string? desiredBarcode = null)
    {
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = "Variant",
            Sku = sku,
            Barcode = barcode,
            PendingShopifySync = pendingShopifySync,
            FailedShopifySyncAttempts = failedShopifySyncAttempts,
            IsActive = isActive,
            IsDeleted = isDeleted
        };
        _dbContext.ShopifyProductVariants.Add(entity);

        // The dispatcher pushes the desired state, not the mirror, and skips any variant that has
        // none — a variant no reconcile has reached yet has nothing decided to push. Seeded to the
        // values the caller asked for, so a test that wants a push to carry particular codes gets
        // them without having to know about the split.
        _dbContext.DesiredItemStates.Add(new DesiredItemStateEntity
        {
            DesiredItemStateId = Guid.NewGuid(),
            ShopifyProductVariantId = entity.ShopifyProductVariantId,
            Sku = desiredSku ?? sku,
            Barcode = desiredBarcode ?? barcode,
            Title = entity.DisplayName
        });

        return entity;
    }
}
