using Application;
using Application.Skulabs.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Skulabs;

public class SkulabsTitleSyncServiceTests : IDisposable
{
    private readonly ISkulabsItemClient _skulabsItemClient = Substitute.For<ISkulabsItemClient>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly ApplicationDbContext _dbContext;

    public SkulabsTitleSyncServiceTests()
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

    // ---------- SyncAll ----------

    [Fact]
    public async Task SyncAll_ShouldReturnEmpty_WhenNoLinkedItemsExist()
    {
        var result = await CreateSut().SyncAll();

        result.ShouldBe(SkulabsTitleSyncResult.Empty);
        await _skulabsItemClient.DidNotReceive()
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
    }

    [Fact]
    public async Task SyncAll_ShouldDoNothing_WhenTitlesAlreadyMatch()
    {
        var variant = SeedVariant(displayName: "Same Title");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Same Title");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Checked.ShouldBe(0);
        result.Drifted.ShouldBe(0);
        result.Corrected.ShouldBe(0);
        await _skulabsItemClient.DidNotReceive()
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SyncAll_ShouldPushVariantDisplayName_WhenTitleDrifts()
    {
        var variant = SeedVariant(displayName: "New Variant Title");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sourceItemId: "src-1", title: "Stale Title");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Checked.ShouldBe(1);
        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(1);
        result.Failed.ShouldBe(0);

        await _skulabsItemClient.Received(1).UpdateItems(
            Arg.Is<IEnumerable<SkulabsItemUpdateWithId>>(updates =>
                updates.Count() == 1
                && updates.First().Id == "src-1"
                && updates.First().Name == "New Variant Title"));

        var storedItem = await _dbContext.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("New Variant Title");

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Count.ShouldBe(1);
        logs[0].Message.ShouldBe(
            "SkuLabs item title corrected to match variant: 'Stale Title' → 'New Variant Title'.");
    }

    [Fact]
    public async Task SyncAll_ShouldBundleMultipleUpdates_IntoASingleBulkCall()
    {
        var v1 = SeedVariant(productId: 100, variantId: 1, displayName: "Title One");
        var v2 = SeedVariant(productId: 200, variantId: 2, displayName: "Title Two");
        SeedSkulabsItem(v1.ShopifyProductVariantId, sourceItemId: "src-1", title: "Old One");
        SeedSkulabsItem(v2.ShopifyProductVariantId, sourceItemId: "src-2", title: "Old Two");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Corrected.ShouldBe(2);
        await _skulabsItemClient.Received(1).UpdateItems(
            Arg.Is<IEnumerable<SkulabsItemUpdateWithId>>(updates => updates.Count() == 2));
    }

    [Fact]
    public async Task SyncAll_ShouldNotMutateLocalRows_WhenSkulabsCallThrows()
    {
        var variant = SeedVariant(displayName: "New Title");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Old Title");
        await _dbContext.SaveChangesAsync();

        _skulabsItemClient
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>())
            .ThrowsAsync(new HttpRequestException("skulabs offline"));

        var result = await CreateSut().SyncAll();

        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(0);
        result.Failed.ShouldBe(1);

        var storedItem = await _dbContext.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Old Title");
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    // ---------- Feature flag ----------

    [Fact]
    public async Task SyncAll_ShouldSkipSkulabsCall_WhenSkulabsWriteBackIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsWriteBack).Returns(false);

        var variant = SeedVariant(displayName: "New Title");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Old Title");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Corrected.ShouldBe(0);
        result.Failed.ShouldBe(1);
        await _skulabsItemClient.DidNotReceive()
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
    }

    [Fact]
    public async Task SyncAll_ShouldNotMutateLocalRow_WhenWriteBackIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsWriteBack).Returns(false);

        var variant = SeedVariant(displayName: "New Title");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Old Title");
        await _dbContext.SaveChangesAsync();

        await CreateSut().SyncAll();

        var storedItem = await _dbContext.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Old Title");
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    // ---------- SyncForVariant ----------

    [Fact]
    public async Task SyncForVariant_ShouldReturnEmpty_WhenVariantNotLinked()
    {
        var variant = SeedVariant(displayName: "Title");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncForVariant(variant.ShopifyProductVariantId);

        result.ShouldBe(SkulabsTitleSyncResult.Empty);
        await _skulabsItemClient.DidNotReceive()
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
    }

    [Fact]
    public async Task SyncForVariant_ShouldDoNothing_WhenAlreadyInSync()
    {
        var variant = SeedVariant(displayName: "Match");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Match");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncForVariant(variant.ShopifyProductVariantId);

        result.Checked.ShouldBe(1);
        result.Drifted.ShouldBe(0);
        result.Corrected.ShouldBe(0);
        await _skulabsItemClient.DidNotReceive()
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
    }

    [Fact]
    public async Task SyncForVariant_ShouldCorrectSkulabs_WhenTitlesDiffer()
    {
        var variant = SeedVariant(displayName: "Variant Title");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sourceItemId: "src-1", title: "SkuLabs Title");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncForVariant(variant.ShopifyProductVariantId);

        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(1);

        await _skulabsItemClient.Received(1).UpdateItems(
            Arg.Is<IEnumerable<SkulabsItemUpdateWithId>>(updates =>
                updates.Count() == 1
                && updates.First().Id == "src-1"
                && updates.First().Name == "Variant Title"));

        var storedItem = await _dbContext.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Variant Title");
    }

    // ---------- SyncForSkulabsItem ----------

    [Fact]
    public async Task SyncForSkulabsItem_ShouldReturnEmpty_WhenItemDoesNotExist()
    {
        var result = await CreateSut().SyncForSkulabsItem(Guid.NewGuid());

        result.ShouldBe(SkulabsTitleSyncResult.Empty);
        await _skulabsItemClient.DidNotReceive()
            .UpdateItems(Arg.Any<IEnumerable<SkulabsItemUpdateWithId>>());
    }

    [Fact]
    public async Task SyncForSkulabsItem_ShouldCorrectSkulabs_WhenTitlesDiffer()
    {
        var variant = SeedVariant(displayName: "Variant Title");
        var item = SeedSkulabsItem(variant.ShopifyProductVariantId, sourceItemId: "src-1", title: "SkuLabs Title");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncForSkulabsItem(item.SkulabsItemId);

        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(1);

        await _skulabsItemClient.Received(1).UpdateItems(
            Arg.Is<IEnumerable<SkulabsItemUpdateWithId>>(updates =>
                updates.Count() == 1
                && updates.First().Id == "src-1"
                && updates.First().Name == "Variant Title"));

        var storedItem = await _dbContext.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Variant Title");
    }

    // ---------- Helpers ----------

    private SkulabsTitleSyncService CreateSut() =>
        new(_dbContext, _skulabsItemClient, _featureManager, NullLogger<SkulabsTitleSyncService>.Instance);

    private async Task<List<ShopifyProductVariantLogEventEntity>> LogsForVariant(Guid variantGuid) =>
        await _dbContext.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid)
            .OrderBy(l => l.CreatedOn)
            .ThenBy(l => l.ShopifyProductVariantLogEventId)
            .ToListAsync();

    private ShopifyProductVariantEntity SeedVariant(
        long productId = 100,
        long variantId = 200,
        string displayName = "Variant",
        string sku = "SKU",
        string barcode = "BAR")
    {
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = displayName,
            Sku = sku,
            Barcode = barcode
        };
        _dbContext.ShopifyProductVariants.Add(entity);
        return entity;
    }

    private SkulabsItemEntity SeedSkulabsItem(
        Guid variantGuid,
        string sourceItemId = "src",
        string sourceListingId = "lst",
        string title = "Title",
        string sku = "sku",
        string barcode = "bar")
    {
        var entity = new SkulabsItemEntity
        {
            SkulabsItemId = Guid.NewGuid(),
            ShopifyProductVariantId = variantGuid,
            SkulabsSourceItemId = sourceItemId,
            SkulabsSourceListingId = sourceListingId,
            Title = title,
            Sku = sku,
            Barcode = barcode
        };
        _dbContext.SkulabsItems.Add(entity);
        return entity;
    }
}
