using Application;
using Application.Sync;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.RateLimiting;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Sync;

public class SkulabsDispatcherTests : IDisposable
{
    private readonly ISkulabsItemClient _skulabsItemClient = Substitute.For<ISkulabsItemClient>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly ApplicationDbContext _dbContext;

    public SkulabsDispatcherTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsWriteBack).Returns(true);
        _skulabsItemClient
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>())
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task DispatchAll_ShouldReturnEmpty_WhenNothingPending()
    {
        SeedPair(pendingSkulabsSync: false);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.ShouldBe(DispatchResult.Empty);
        await _skulabsItemClient.DidNotReceive().UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
    }

    [Fact]
    public async Task DispatchAll_ShouldPushPendingTitle_AndClearFlag()
    {
        SeedPair(sourceItemId: "src-1", title: "Authoritative Title", pendingSkulabsSync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.Pending.ShouldBe(1);
        result.Pushed.ShouldBe(1);
        result.Failed.ShouldBe(0);

        await _skulabsItemClient.Received(1).UpdateItems(
            Arg.Is<IEnumerable<SkulabsItemUpdateWithId>>(updates =>
                updates.Count() == 1
                && updates.First().Id == "src-1"
                && updates.First().Name == "Authoritative Title"));

        (await _dbContext.SkulabsItems.SingleAsync()).PendingSkulabsSync.ShouldBeFalse();
    }

    [Fact]
    public async Task DispatchAll_ShouldBundleAllPendingItems_IntoASingleBulkCall()
    {
        SeedPair(productId: 1, variantId: 1, sourceItemId: "src-1", sourceListingId: "lst-1", pendingSkulabsSync: true);
        SeedPair(productId: 2, variantId: 2, sourceItemId: "src-2", sourceListingId: "lst-2", pendingSkulabsSync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.Pushed.ShouldBe(2);
        await _skulabsItemClient.Received(1).UpdateItems(
            Arg.Is<IEnumerable<SkulabsItemUpdateWithId>>(updates => updates.Count() == 2));
    }

    [Fact]
    public async Task DispatchAll_ShouldSkipSkulabs_AndKeepPending_WhenKillSwitchOff()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsWriteBack).Returns(false);
        SeedPair(pendingSkulabsSync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.Pending.ShouldBe(1);
        result.Pushed.ShouldBe(0);
        result.Failed.ShouldBe(0);
        await _skulabsItemClient.DidNotReceive().UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());

        (await _dbContext.SkulabsItems.SingleAsync()).PendingSkulabsSync.ShouldBeTrue();
    }

    [Fact]
    public async Task DispatchAll_ShouldReportRateLimited_WithoutTouchingCounters()
    {
        SeedPair(pendingSkulabsSync: true, failedSkulabsSyncAttempts: 1);
        await _dbContext.SaveChangesAsync();

        _skulabsItemClient
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>())
            .ThrowsAsync(new RateLimitedException("skulabs", TimeSpan.FromSeconds(180)));

        var result = await CreateSut().DispatchAll();

        result.RateLimited.ShouldBeTrue();
        result.RetryAfter.ShouldBe(TimeSpan.FromSeconds(180));
        result.Pushed.ShouldBe(0);
        result.Failed.ShouldBe(0);

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.PendingSkulabsSync.ShouldBeTrue();
        // Rate limiting means "later", not "broken" — the counter must not move.
        stored.FailedSkulabsSyncAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchAll_ShouldIncrementCountersOnWholeBatch_WhenPushThrows()
    {
        SeedPair(productId: 1, variantId: 1, sourceItemId: "src-1", sourceListingId: "lst-1", pendingSkulabsSync: true);
        SeedPair(productId: 2, variantId: 2, sourceItemId: "src-2", sourceListingId: "lst-2", pendingSkulabsSync: true);
        await _dbContext.SaveChangesAsync();

        _skulabsItemClient
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>())
            .ThrowsAsync(new HttpRequestException("skulabs offline"));

        var result = await CreateSut().DispatchAll();

        result.Failed.ShouldBe(2);

        var items = await _dbContext.SkulabsItems.ToListAsync();
        items.ShouldAllBe(i => i.PendingSkulabsSync && i.FailedSkulabsSyncAttempts == 1);
    }

    [Fact]
    public async Task DispatchAll_ShouldExcludeItem_AfterThreeConsecutiveFailures()
    {
        var (variant, _) = SeedPair(pendingSkulabsSync: true, failedSkulabsSyncAttempts: 2);
        await _dbContext.SaveChangesAsync();

        _skulabsItemClient
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>())
            .ThrowsAsync(new HttpRequestException("skulabs offline"));

        await CreateSut().DispatchAll();

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.FailedSkulabsSyncAttempts.ShouldBe(3);

        var logs = await _dbContext.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variant.ShopifyProductVariantId)
            .ToListAsync();
        logs.ShouldContain(l =>
            l.Message == "Linked SkuLabs item excluded from sync after 3 consecutive failed SkuLabs push attempts.");

        // The excluded item is skipped by the next run — no further SkuLabs call.
        _skulabsItemClient.ClearReceivedCalls();
        var secondRun = await CreateSut().DispatchAll();
        secondRun.ShouldBe(DispatchResult.Empty);
        await _skulabsItemClient.DidNotReceive().UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
    }

    [Fact]
    public async Task DispatchAll_ShouldResetCounter_OnSuccessfulPush()
    {
        SeedPair(pendingSkulabsSync: true, failedSkulabsSyncAttempts: 2);
        await _dbContext.SaveChangesAsync();

        await CreateSut().DispatchAll();

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.FailedSkulabsSyncAttempts.ShouldBe(0);
        stored.PendingSkulabsSync.ShouldBeFalse();
    }

    [Fact]
    public async Task DispatchAll_ShouldExcludeItemsOfInactiveOrDeletedVariants()
    {
        SeedPair(productId: 1, variantId: 1, sourceItemId: "src-1", sourceListingId: "lst-1",
            pendingSkulabsSync: true, variantIsActive: false);
        SeedPair(productId: 2, variantId: 2, sourceItemId: "src-2", sourceListingId: "lst-2",
            pendingSkulabsSync: true, variantIsDeleted: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchAll();

        result.ShouldBe(DispatchResult.Empty);
    }

    [Fact]
    public async Task DispatchVariants_ShouldOnlyPushItemsLinkedToTheGivenVariants()
    {
        var (target, _) = SeedPair(productId: 1, variantId: 1, sourceItemId: "src-1", sourceListingId: "lst-1",
            pendingSkulabsSync: true);
        SeedPair(productId: 2, variantId: 2, sourceItemId: "src-2", sourceListingId: "lst-2",
            pendingSkulabsSync: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().DispatchVariants([target.ShopifyProductVariantId]);

        result.Pushed.ShouldBe(1);
        await _skulabsItemClient.Received(1).UpdateItems(
            Arg.Is<IEnumerable<SkulabsItemUpdateWithId>>(updates =>
                updates.Count() == 1 && updates.First().Id == "src-1"));
    }

    // ---------- Helpers ----------

    private SkulabsDispatcher CreateSut() =>
        new(_dbContext, _skulabsItemClient, _featureManager, NullLogger<SkulabsDispatcher>.Instance);

    private (ShopifyProductVariantEntity Variant, SkulabsItemEntity Item) SeedPair(
        long productId = 100,
        long variantId = 200,
        string sourceItemId = "src",
        string sourceListingId = "lst",
        string title = "Title",
        bool pendingSkulabsSync = false,
        int failedSkulabsSyncAttempts = 0,
        bool variantIsActive = true,
        bool variantIsDeleted = false)
    {
        var variant = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = title,
            Sku = "SKU",
            Barcode = "BAR",
            IsActive = variantIsActive,
            IsDeleted = variantIsDeleted
        };
        _dbContext.ShopifyProductVariants.Add(variant);

        var item = new SkulabsItemEntity
        {
            SkulabsItemId = Guid.NewGuid(),
            SkulabsSourceItemId = sourceItemId,
            Title = title,
            Sku = "SKU",
            Barcode = "BAR",
            PendingSkulabsSync = pendingSkulabsSync,
            FailedSkulabsSyncAttempts = failedSkulabsSyncAttempts,
            Listings =
            {
                new SkulabsItemListingEntity
                {
                    SkulabsItemListingId = Guid.NewGuid(),
                    SkulabsSourceListingId = sourceListingId,
                    RawVariantId = variantId.ToString(),
                    ShopifyProductId = productId.ToString(),
                    ShopifyProductVariantId = variant.ShopifyProductVariantId
                }
            }
        };
        _dbContext.SkulabsItems.Add(item);

        return (variant, item);
    }
}
