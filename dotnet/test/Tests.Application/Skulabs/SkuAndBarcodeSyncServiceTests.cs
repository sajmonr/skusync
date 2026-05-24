using Application;
using Application.Skulabs.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Skulabs;

public class SkuAndBarcodeSyncServiceTests : IDisposable
{
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<SkuAndBarcodeSyncService> _logger = new();

    public SkuAndBarcodeSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        // Default: Shopify writes are enabled and accepted.
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(true);
        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);
    }

    public void Dispose() => _dbContext.Dispose();

    // ---------- SyncAll ----------

    [Fact]
    public async Task SyncAll_ShouldReturnEmpty_WhenNoLinkedItemsExist()
    {
        var result = await CreateSut().SyncAll();

        result.ShouldBe(SkuAndBarcodeSyncResult.Empty);
        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task SyncAll_ShouldDoNothing_WhenLinkedItemsAreInSync()
    {
        var variant = SeedVariant(sku: "matching-sku", barcode: "matching-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "matching-sku", barcode: "matching-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Checked.ShouldBe(0);
        result.Drifted.ShouldBe(0);
        result.Corrected.ShouldBe(0);
        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SyncAll_ShouldPushSkulabsValuesToShopify_WhenSkuDrifts()
    {
        var variant = SeedVariant(sku: "shopify-old", barcode: "matching-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "skulabs-authoritative", barcode: "matching-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Checked.ShouldBe(1);
        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(1);
        result.Failed.ShouldBe(0);

        await _shopifyProductService.Received(1).UpdateVariants(
            variant.GlobalProductId,
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(updates =>
                updates.Count() == 1
                && updates.First().GlobalVariantId == variant.GlobalVariantId
                && updates.First().Sku == "skulabs-authoritative"
                && updates.First().Barcode == "matching-bar"));

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.Sku.ShouldBe("skulabs-authoritative");
        stored.Barcode.ShouldBe("matching-bar");

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Count.ShouldBe(1);
        logs[0].Message.ShouldBe("SKU corrected to match SkuLabs: 'shopify-old' → 'skulabs-authoritative'.");
    }

    [Fact]
    public async Task SyncAll_ShouldEmitTwoLogs_WhenBothSkuAndBarcodeDrift()
    {
        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        await CreateSut().SyncAll();

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Select(l => l.Message).ShouldBe([
            "SKU corrected to match SkuLabs: 'old-sku' → 'new-sku'.",
            "Barcode corrected to match SkuLabs: 'old-bar' → 'new-bar'."
        ]);
    }

    [Fact]
    public async Task SyncAll_ShouldBatchUpdatesPerProduct()
    {
        // Two variants on the *same* Shopify product, both drifting → one Shopify mutation call.
        var v1 = SeedVariant(productId: 100, variantId: 1, sku: "old-1", barcode: "b-1");
        var v2 = SeedVariant(productId: 100, variantId: 2, sku: "old-2", barcode: "b-2");
        SeedSkulabsItem(v1.ShopifyProductVariantId, sku: "new-1", barcode: "b-1");
        SeedSkulabsItem(v2.ShopifyProductVariantId, sku: "new-2", barcode: "b-2");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Corrected.ShouldBe(2);
        await _shopifyProductService.Received(1).UpdateVariants(
            v1.GlobalProductId,
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(updates => updates.Count() == 2));
    }

    [Fact]
    public async Task SyncAll_ShouldNotMutateLocalRow_WhenShopifyUpdateFails()
    {
        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(false);

        var result = await CreateSut().SyncAll();

        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(0);
        result.Failed.ShouldBe(1);

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.Sku.ShouldBe("old-sku");
        stored.Barcode.ShouldBe("old-bar");
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SyncAll_ShouldNotMutateLocalRow_WhenShopifyUpdateThrows()
    {
        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .ThrowsAsync(new HttpRequestException("shopify offline"));

        var result = await CreateSut().SyncAll();

        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(0);
        result.Failed.ShouldBe(1);

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.Sku.ShouldBe("old-sku");
        stored.Barcode.ShouldBe("old-bar");
        stored.PendingShopifySync.ShouldBeFalse();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SyncAll_ShouldCorrectGroupB_WhenGroupAThrows()
    {
        // Transient failure on product A's Shopify call must not abort the whole sweep —
        // product B still needs to be reconciled.
        var a = SeedVariant(productId: 100, variantId: 1, sku: "a-old", barcode: "a-bar");
        var b = SeedVariant(productId: 200, variantId: 2, sku: "b-old", barcode: "b-bar");
        SeedSkulabsItem(a.ShopifyProductVariantId, sku: "a-new", barcode: "a-bar");
        SeedSkulabsItem(b.ShopifyProductVariantId, sku: "b-new", barcode: "b-bar");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.UpdateVariants(a.GlobalProductId, Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .ThrowsAsync(new HttpRequestException("shopify offline"));
        _shopifyProductService.UpdateVariants(b.GlobalProductId, Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);

        var result = await CreateSut().SyncAll();

        result.Corrected.ShouldBe(1);
        result.Failed.ShouldBe(1);

        var aStored = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.ShopifyProductVariantId == a.ShopifyProductVariantId);
        aStored.Sku.ShouldBe("a-old");

        var bStored = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.ShopifyProductVariantId == b.ShopifyProductVariantId);
        bStored.Sku.ShouldBe("b-new");
    }

    [Fact]
    public async Task SyncAll_ShouldCorrectGroupB_WhenGroupAUpdateFails()
    {
        // Two products. Product A's update returns false, Product B's returns true.
        // Product B should still be corrected locally.
        var a = SeedVariant(productId: 100, variantId: 1, sku: "a-old", barcode: "a-bar");
        var b = SeedVariant(productId: 200, variantId: 2, sku: "b-old", barcode: "b-bar");
        SeedSkulabsItem(a.ShopifyProductVariantId, sku: "a-new", barcode: "a-bar");
        SeedSkulabsItem(b.ShopifyProductVariantId, sku: "b-new", barcode: "b-bar");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.UpdateVariants(a.GlobalProductId, Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(false);
        _shopifyProductService.UpdateVariants(b.GlobalProductId, Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(true);

        var result = await CreateSut().SyncAll();

        result.Drifted.ShouldBe(2);
        result.Corrected.ShouldBe(1);
        result.Failed.ShouldBe(1);

        var aStored = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.ShopifyProductVariantId == a.ShopifyProductVariantId);
        aStored.Sku.ShouldBe("a-old");

        var bStored = await _dbContext.ShopifyProductVariants.SingleAsync(v => v.ShopifyProductVariantId == b.ShopifyProductVariantId);
        bStored.Sku.ShouldBe("b-new");
    }

    // ---------- Feature flag scoping (gates Shopify, not local correction) ----------

    [Fact]
    public async Task SyncAll_ShouldSkipShopifyCall_WhenShopifyWriteBackIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(false);

        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        await CreateSut().SyncAll();

        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task SyncAll_ShouldStillUpdateLocalDatabase_WhenShopifyWriteBackIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(false);

        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(1);
        result.Failed.ShouldBe(0);

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.Sku.ShouldBe("new-sku");
        stored.Barcode.ShouldBe("new-bar");

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Select(l => l.Message).ShouldBe([
            "SKU corrected to match SkuLabs: 'old-sku' → 'new-sku'.",
            "Barcode corrected to match SkuLabs: 'old-bar' → 'new-bar'."
        ]);
    }

    [Fact]
    public async Task SyncAll_ShouldMarkPendingShopifySync_WhenWriteBackIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyWriteBack).Returns(false);

        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        await CreateSut().SyncAll();

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.PendingShopifySync.ShouldBeTrue();
    }

    [Fact]
    public async Task SyncAll_ShouldClearPendingShopifySync_AfterSuccessfulShopifyPush()
    {
        // Set up a row that's locally consistent with SkuLabs but flagged for a pending push
        // (the typical state after a write-back-disabled correction).
        var variant = SeedVariant(sku: "new-sku", barcode: "new-bar", pendingShopifySync: true);
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncAll();

        // Pending-only — not technically drifted, but still corrected (Shopify was updated).
        result.Drifted.ShouldBe(0);
        result.Corrected.ShouldBe(1);

        await _shopifyProductService.Received(1).UpdateVariants(
            variant.GlobalProductId,
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(updates =>
                updates.Count() == 1
                && updates.First().Sku == "new-sku"
                && updates.First().Barcode == "new-bar"));

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.PendingShopifySync.ShouldBeFalse();
    }

    [Fact]
    public async Task SyncAll_ShouldNotEmitChangeLogs_ForPendingOnlyCandidate()
    {
        // Pending-only means local already matches SkuLabs — applying the correction locally
        // is a no-op for SKU/barcode, so no Sku/Barcode log events should be added.
        var variant = SeedVariant(sku: "x", barcode: "y", pendingShopifySync: true);
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "x", barcode: "y");
        await _dbContext.SaveChangesAsync();

        await CreateSut().SyncAll();

        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SyncAll_ShouldLeavePendingFlag_WhenShopifyRejectsPush()
    {
        var variant = SeedVariant(sku: "x", barcode: "y", pendingShopifySync: true);
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "x", barcode: "y");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>())
            .Returns(false);

        var result = await CreateSut().SyncAll();

        result.Corrected.ShouldBe(0);
        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.PendingShopifySync.ShouldBeTrue();
    }

    // ---------- SyncForSkulabsItem ----------

    [Fact]
    public async Task SyncForSkulabsItem_ShouldReturnEmpty_WhenItemDoesNotExist()
    {
        var result = await CreateSut().SyncForSkulabsItem(Guid.NewGuid());

        result.ShouldBe(SkuAndBarcodeSyncResult.Empty);
        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task SyncForSkulabsItem_ShouldDoNothing_WhenItemMatchesVariant()
    {
        var variant = SeedVariant(sku: "same", barcode: "same");
        var item = SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "same", barcode: "same");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncForSkulabsItem(item.SkulabsItemId);

        result.Checked.ShouldBe(1);
        result.Drifted.ShouldBe(0);
        result.Corrected.ShouldBe(0);
        await _shopifyProductService.DidNotReceive()
            .UpdateVariants(Arg.Any<string>(), Arg.Any<IEnumerable<ShopifyUpdateProductVariant>>());
    }

    [Fact]
    public async Task SyncForSkulabsItem_ShouldCorrectShopify_WhenItemDrifts()
    {
        var variant = SeedVariant(sku: "shopify-side", barcode: "shopify-bar");
        var item = SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "skulabs-side", barcode: "skulabs-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().SyncForSkulabsItem(item.SkulabsItemId);

        result.Drifted.ShouldBe(1);
        result.Corrected.ShouldBe(1);

        await _shopifyProductService.Received(1).UpdateVariants(
            variant.GlobalProductId,
            Arg.Is<IEnumerable<ShopifyUpdateProductVariant>>(updates =>
                updates.Count() == 1
                && updates.First().Sku == "skulabs-side"
                && updates.First().Barcode == "skulabs-bar"));

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.Sku.ShouldBe("skulabs-side");
        stored.Barcode.ShouldBe("skulabs-bar");
    }

    // ---------- Helpers ----------

    private SkuAndBarcodeSyncService CreateSut() =>
        new(_dbContext, _shopifyProductService, _featureManager, _logger);

    private async Task<List<ShopifyProductVariantLogEventEntity>> LogsForVariant(Guid variantGuid) =>
        await _dbContext.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid)
            .OrderBy(l => l.CreatedOn)
            .ThenBy(l => l.ShopifyProductVariantLogEventId)
            .ToListAsync();

    private ShopifyProductVariantEntity SeedVariant(
        long productId = 100,
        long variantId = 200,
        string sku = "SKU",
        string barcode = "BAR",
        bool pendingShopifySync = false)
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
            PendingShopifySync = pendingShopifySync
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

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
