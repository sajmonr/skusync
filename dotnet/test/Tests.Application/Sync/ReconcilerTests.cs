using Application.Sync;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Tests.Application.Sync;

public class ReconcilerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public ReconcilerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    // ---------- SKU/barcode rule (SkuLabs authoritative) ----------

    [Fact]
    public async Task ReconcileAll_ShouldReturnEmpty_WhenNoLinkedItemsExist()
    {
        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
    }

    [Fact]
    public async Task ReconcileAll_ShouldDoNothing_WhenPairsAreInSync()
    {
        var variant = SeedVariant(displayName: "Same", sku: "matching-sku", barcode: "matching-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Same", sku: "matching-sku", barcode: "matching-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.PendingShopifySync.ShouldBeFalse();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ReconcileAll_ShouldMirrorSkuAndMarkVariantPending_WhenSkuDrifts()
    {
        var variant = SeedVariant(sku: "shopify-old", barcode: "matching-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "skulabs-authoritative", barcode: "matching-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.VariantsMarked.ShouldBe(1);
        result.ItemsMarked.ShouldBe(0);

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.Sku.ShouldBe("skulabs-authoritative");
        stored.PendingShopifySync.ShouldBeTrue();

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Select(l => l.Message).ShouldBe([
            "SKU corrected to match SkuLabs: 'shopify-old' → 'skulabs-authoritative'."
        ]);
    }

    [Fact]
    public async Task ReconcileAll_ShouldEmitTwoLogs_WhenBothSkuAndBarcodeDrift()
    {
        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileAll();

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Select(l => l.Message).ShouldBe([
            "SKU corrected to match SkuLabs: 'old-sku' → 'new-sku'.",
            "Barcode corrected to match SkuLabs: 'old-bar' → 'new-bar'."
        ]);
    }

    [Fact]
    public async Task ReconcileAll_ShouldNotTreatBlankSkulabsSku_AsDrift()
    {
        var variant = SeedVariant(sku: "good-sku", barcode: "matching-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "", barcode: "matching-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
        (await _dbContext.ShopifyProductVariants.SingleAsync()).Sku.ShouldBe("good-sku");
    }

    [Fact]
    public async Task ReconcileAll_ShouldNotOverwriteVariantSku_WhenSkulabsSkuIsBlank()
    {
        // Barcode drifts (legitimate candidate) but the SkuLabs SKU is blank — the good variant
        // SKU must be preserved while the barcode is corrected.
        var variant = SeedVariant(sku: "good-sku", barcode: "old-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileAll();

        var stored = await _dbContext.ShopifyProductVariants.SingleAsync();
        stored.Sku.ShouldBe("good-sku");
        stored.Barcode.ShouldBe("new-bar");
        stored.PendingShopifySync.ShouldBeTrue();
    }

    [Fact]
    public async Task ReconcileAll_ShouldNotTreatBlankSkulabsBarcode_AsDrift()
    {
        var variant = SeedVariant(sku: "matching-sku", barcode: "good-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "matching-sku", barcode: "");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
        (await _dbContext.ShopifyProductVariants.SingleAsync()).Barcode.ShouldBe("good-bar");
    }

    // ---------- Title rule (variant DisplayName authoritative) ----------

    [Fact]
    public async Task ReconcileAll_ShouldMirrorTitleAndMarkItemPending_WhenTitleDrifts()
    {
        var variant = SeedVariant(displayName: "New Variant Title");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Stale Title");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.VariantsMarked.ShouldBe(0);
        result.ItemsMarked.ShouldBe(1);

        var storedItem = await _dbContext.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("New Variant Title");
        storedItem.PendingSkulabsSync.ShouldBeTrue();

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Select(l => l.Message).ShouldBe([
            "SkuLabs item title corrected to match variant: 'Stale Title' → 'New Variant Title'."
        ]);
    }

    [Fact]
    public async Task ReconcileAll_ShouldMarkBothSides_WhenSkuAndTitleDrift()
    {
        var variant = SeedVariant(displayName: "Authoritative Title", sku: "old-sku");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Old Title", sku: "new-sku");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.VariantsMarked.ShouldBe(1);
        result.ItemsMarked.ShouldBe(1);

        var storedVariant = await _dbContext.ShopifyProductVariants.SingleAsync();
        storedVariant.Sku.ShouldBe("new-sku");
        storedVariant.PendingShopifySync.ShouldBeTrue();

        var storedItem = await _dbContext.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Authoritative Title");
        storedItem.PendingSkulabsSync.ShouldBeTrue();
    }

    // ---------- Exclusions ----------

    [Fact]
    public async Task ReconcileAll_ShouldExcludeInactiveAndDeletedVariants()
    {
        var inactive = SeedVariant(productId: 1, variantId: 1, sku: "old-a", isActive: false);
        SeedSkulabsItem(inactive.ShopifyProductVariantId, sourceItemId: "src-a", sourceListingId: "lst-a", sku: "new-a");
        var deleted = SeedVariant(productId: 2, variantId: 2, sku: "old-b", isDeleted: true);
        SeedSkulabsItem(deleted.ShopifyProductVariantId, sourceItemId: "src-b", sourceListingId: "lst-b", sku: "new-b");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
    }

    // ---------- Scoped entry points ----------

    [Fact]
    public async Task ReconcileVariants_ShouldOnlyTouchTheGivenVariants()
    {
        var target = SeedVariant(productId: 1, variantId: 1, sku: "old-1");
        SeedSkulabsItem(target.ShopifyProductVariantId, sourceItemId: "src-1", sourceListingId: "lst-1", sku: "new-1");
        var other = SeedVariant(productId: 2, variantId: 2, sku: "old-2");
        SeedSkulabsItem(other.ShopifyProductVariantId, sourceItemId: "src-2", sourceListingId: "lst-2", sku: "new-2");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileVariants([target.ShopifyProductVariantId]);

        result.VariantsMarked.ShouldBe(1);
        (await _dbContext.ShopifyProductVariants
            .SingleAsync(v => v.ShopifyProductVariantId == other.ShopifyProductVariantId))
            .PendingShopifySync.ShouldBeFalse();
    }

    [Fact]
    public async Task ReconcileVariants_ShouldReturnEmpty_ForEmptyScope()
    {
        var result = await CreateSut().ReconcileVariants([]);

        result.ShouldBe(ReconcileResult.Empty);
    }

    [Fact]
    public async Task ReconcileSkulabsItems_ShouldReconcileTheLinkedPair()
    {
        var variant = SeedVariant(displayName: "Variant Title", sku: "old-sku");
        var item = SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Item Title", sku: "new-sku");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileSkulabsItems([item.SkulabsItemId]);

        result.VariantsMarked.ShouldBe(1);
        result.ItemsMarked.ShouldBe(1);
    }

    // ---------- Helpers ----------

    private Reconciler CreateSut() => new(_dbContext, NullLogger<Reconciler>.Instance);

    private async Task<List<ShopifyProductVariantLogEventEntity>> LogsForVariant(Guid variantGuid) =>
        await _dbContext.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid)
            .OrderBy(l => l.CreatedOn)
            .ThenBy(l => l.ShopifyProductVariantLogEventId)
            .ToListAsync();

    private ShopifyProductVariantEntity SeedVariant(
        long productId = 100,
        long variantId = 200,
        string displayName = "Title",
        string sku = "SKU",
        string barcode = "BAR",
        bool isActive = true,
        bool isDeleted = false)
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
            Barcode = barcode,
            IsActive = isActive,
            IsDeleted = isDeleted
        };
        _dbContext.ShopifyProductVariants.Add(entity);
        return entity;
    }

    private SkulabsItemEntity SeedSkulabsItem(
        Guid variantGuid,
        string sourceItemId = "src",
        string sourceListingId = "lst",
        string title = "Title",
        string sku = "SKU",
        string barcode = "BAR")
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
