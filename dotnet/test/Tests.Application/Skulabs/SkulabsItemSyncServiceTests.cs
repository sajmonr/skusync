using Application.Skulabs.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Skulabs.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Tests.Application.Skulabs;

public class SkulabsItemSyncServiceTests : IDisposable
{
    private readonly ISkulabsItemClient _skulabsClient = Substitute.For<ISkulabsItemClient>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<SkulabsItemSyncService> _logger = new();

    public SkulabsItemSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    // ---------- Basic flow ----------

    [Fact]
    public async Task Sync_ShouldReturnEmpty_WhenNoItemsFromSkulabs()
    {
        _skulabsClient.GetAllItems().Returns([]);
        var sut = CreateSut();

        var result = await sut.Sync();

        result.CreatedSkulabsItemIds.ShouldBeEmpty();
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();
        result.UnmatchedCount.ShouldBe(0);
        result.SkippedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Sync_ShouldSkip_WhenNoMatchingVariantInDatabase()
    {
        _skulabsClient.GetAllItems().Returns([NewSkulabsItem(variantId: "999")]);
        var sut = CreateSut();

        var result = await sut.Sync();

        result.UnmatchedCount.ShouldBe(1);
        result.CreatedSkulabsItemIds.ShouldBeEmpty();
        (await _dbContext.SkulabsItems.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sync_ShouldSkipItem_WhenSkulabsVariantIdIsNotNumeric()
    {
        SeedVariant(variantId: 200L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([NewSkulabsItem(variantId: "not-a-number")]);
        var sut = CreateSut();

        var result = await sut.Sync();

        result.SkippedCount.ShouldBe(1);
        result.UnmatchedCount.ShouldBe(0);
        (await _dbContext.SkulabsItems.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sync_ShouldProcessRemainingItems_WhenOneItemFails()
    {
        SeedVariant(variantId: 200L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([
            NewSkulabsItem(variantId: "bad"),
            NewSkulabsItem(itemId: "src-good", listingId: "lst-good", variantId: "200")
        ]);
        var sut = CreateSut();

        var result = await sut.Sync();

        result.SkippedCount.ShouldBe(1);
        result.CreatedSkulabsItemIds.Count.ShouldBe(1);
        (await _dbContext.SkulabsItems.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Sync_ShouldUseShopifyVariantIdAndNotSkuOrBarcode_ForMatching()
    {
        // Variant has matching SKU/barcode values but a *different* VariantId.
        // Must not match — matching is by variant id only.
        SeedVariant(variantId: 999L, sku: "shared-sku", barcode: "shared-barcode");
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([
            new SkuLabsItem("src", "lst", "200", "shared-sku", "shared-barcode", "Title")
        ]);
        var sut = CreateSut();

        var result = await sut.Sync();

        result.UnmatchedCount.ShouldBe(1);
        (await _dbContext.SkulabsItems.CountAsync()).ShouldBe(0);
    }

    // ---------- Case 1: variant has no Skulabs item linked ----------

    [Fact]
    public async Task Sync_ShouldCreateSkulabsItem_WhenMatchingVariantHasNoneInDatabase()
    {
        var variant = SeedVariant(variantId: 45696210862241L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([
            NewSkulabsItem(
                itemId: "69b4543c6642ed434a5b1c4a",
                listingId: "69b454b06642ed434a5bf571",
                variantId: "45696210862241",
                sku: "1 bird",
                barcode: "10862241",
                title: "Yellow Vintage Nature Domino Necklace (Goose (1bird))")
        ]);
        var sut = CreateSut();

        var result = await sut.Sync();

        result.CreatedSkulabsItemIds.Count.ShouldBe(1);
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.ShopifyProductVariantId.ShouldBe(variant.ShopifyProductVariantId);
        stored.SkulabsSourceItemId.ShouldBe("69b4543c6642ed434a5b1c4a");
        stored.SkulabsSourceListingId.ShouldBe("69b454b06642ed434a5bf571");
        stored.Sku.ShouldBe("1 bird");
        stored.Barcode.ShouldBe("10862241");
        stored.Title.ShouldBe("Yellow Vintage Nature Domino Necklace (Goose (1bird))");
        result.CreatedSkulabsItemIds.ShouldContain(stored.SkulabsItemId);
    }

    [Fact]
    public async Task Sync_ShouldAddLinkedLogOnVariant_WhenCreatingNewLink()
    {
        var variant = SeedVariant(variantId: 200L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([
            NewSkulabsItem(itemId: "skulabs-1", variantId: "200")
        ]);

        await CreateSut().Sync();

        var logs = await _dbContext.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variant.ShopifyProductVariantId)
            .ToListAsync();
        logs.Count.ShouldBe(1);
        logs[0].Message.ShouldBe("Linked to SkuLabs item 'skulabs-1'.");
    }

    // ---------- Case 2: variant has the same Skulabs item linked (no-op) ----------

    [Fact]
    public async Task Sync_ShouldBeNoOp_WhenLinkIsAlreadyIdentical()
    {
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem(variant.ShopifyProductVariantId,
            sourceItemId: "src-1", sourceListingId: "lst-1",
            title: "Same Title", sku: "same-sku", barcode: "same-bar");
        await _dbContext.SaveChangesAsync();
        var originalId = existing.SkulabsItemId;

        _skulabsClient.GetAllItems().Returns([
            new SkuLabsItem("src-1", "lst-1", "200", "same-sku", "same-bar", "Same Title")
        ]);

        var result = await CreateSut().Sync();

        result.CreatedSkulabsItemIds.ShouldBeEmpty();
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.SkulabsItemId.ShouldBe(originalId);
    }

    [Fact]
    public async Task Sync_ShouldNotRefreshMetadata_WhenLinkIdsMatchButMetadataDiffers()
    {
        // Same SkuLabs source id and same variant — only title/sku/barcode differ.
        // Per the contract, the row is NOT touched.
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem(variant.ShopifyProductVariantId,
            sourceItemId: "src-1", sourceListingId: "lst-old",
            title: "Old Title", sku: "old-sku", barcode: "old-bar");
        await _dbContext.SaveChangesAsync();
        var originalId = existing.SkulabsItemId;

        _skulabsClient.GetAllItems().Returns([
            new SkuLabsItem("src-1", "lst-new", "200", "new-sku", "new-bar", "New Title")
        ]);

        var result = await CreateSut().Sync();

        result.CreatedSkulabsItemIds.ShouldBeEmpty();
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.SkulabsItemId.ShouldBe(originalId);
        stored.SkulabsSourceListingId.ShouldBe("lst-old");
        stored.Title.ShouldBe("Old Title");
        stored.Sku.ShouldBe("old-sku");
        stored.Barcode.ShouldBe("old-bar");
    }

    // ---------- Case 3a: same SkuLabs item moved to a different variant (re-link) ----------

    [Fact]
    public async Task Sync_ShouldReLink_WhenSkulabsItemMovesToDifferentVariant()
    {
        // DB: V1 ↔ S2.  API: V3 ↔ S2.  Expected: link V1↔S2 severed, link V3↔S2 created
        // (as a re-pointed row — PK preserved).
        var variantV1 = SeedVariant(variantId: 1L);
        var variantV3 = SeedVariant(variantId: 3L);
        var existing = SeedSkulabsItem(variantV1.ShopifyProductVariantId,
            sourceItemId: "S2", sourceListingId: "L-old",
            title: "Old Title", sku: "old-sku", barcode: "old-bar");
        await _dbContext.SaveChangesAsync();
        var preservedRowId = existing.SkulabsItemId;

        _skulabsClient.GetAllItems().Returns([
            new SkuLabsItem("S2", "L-new", "3", "new-sku", "new-bar", "New Title")
        ]);

        var result = await CreateSut().Sync();

        result.UpdatedSkulabsItemIds.ShouldContain(preservedRowId);
        result.CreatedSkulabsItemIds.ShouldBeEmpty();

        // Exactly one row, with the same PK, now pointing at V3, metadata refreshed.
        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.SkulabsItemId.ShouldBe(preservedRowId);
        stored.ShopifyProductVariantId.ShouldBe(variantV3.ShopifyProductVariantId);
        stored.SkulabsSourceListingId.ShouldBe("L-new");
        stored.Title.ShouldBe("New Title");
        stored.Sku.ShouldBe("new-sku");
        stored.Barcode.ShouldBe("new-bar");

        // Old variant got "unlinked", new variant got "linked".
        var v1Logs = await LogsForVariant(variantV1.ShopifyProductVariantId);
        v1Logs.Count.ShouldBe(1);
        v1Logs[0].Message.ShouldBe("Unlinked from SkuLabs item 'S2'.");

        var v3Logs = await LogsForVariant(variantV3.ShopifyProductVariantId);
        v3Logs.Count.ShouldBe(1);
        v3Logs[0].Message.ShouldBe("Linked to SkuLabs item 'S2'.");
    }

    // ---------- Case 3b: variant has a *different* SkuLabs item linked (replace) ----------

    [Fact]
    public async Task Sync_ShouldReplaceLink_WhenVariantGetsDifferentSkulabsItem()
    {
        // DB: V200 ↔ S-old.  API: V200 ↔ S-new.  Expected: old row deleted, new row created.
        var variant = SeedVariant(variantId: 200L);
        SeedSkulabsItem(variant.ShopifyProductVariantId,
            sourceItemId: "S-old", sourceListingId: "lst-old",
            title: "Old", sku: "old-sku", barcode: "old-bar");
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([
            new SkuLabsItem("S-new", "lst-new", "200", "new-sku", "new-bar", "New")
        ]);

        var result = await CreateSut().Sync();

        result.CreatedSkulabsItemIds.Count.ShouldBe(1);
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.SkulabsSourceItemId.ShouldBe("S-new");
        stored.ShopifyProductVariantId.ShouldBe(variant.ShopifyProductVariantId);
        stored.Title.ShouldBe("New");
        stored.Sku.ShouldBe("new-sku");

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        // Order matters: sever first, then link.
        logs.Select(l => l.Message).ShouldBe([
            "Unlinked from SkuLabs item 'S-old'.",
            "Linked to SkuLabs item 'S-new'."
        ]);
    }

    // ---------- Edge case: two SkuLabs items swap variants in the same batch ----------

    [Fact]
    public async Task Sync_ShouldHandleSwap_WhenTwoSkulabsItemsExchangeVariants()
    {
        // DB:  V1↔SA,  V2↔SB.  API:  V1↔SB,  V2↔SA.
        //
        // Note: a single transaction can't re-point both rows in place — the unique index
        // on ShopifyProductVariantId would be violated mid-transaction. The reconciler will
        // sever one row and re-create it; the other is re-pointed. The *end state* is what
        // matters and is asserted here.
        var v1 = SeedVariant(variantId: 1L);
        var v2 = SeedVariant(variantId: 2L);
        SeedSkulabsItem(v1.ShopifyProductVariantId,
            sourceItemId: "SA", sourceListingId: "lA",
            title: "A", sku: "ska", barcode: "bca");
        SeedSkulabsItem(v2.ShopifyProductVariantId,
            sourceItemId: "SB", sourceListingId: "lB",
            title: "B", sku: "skb", barcode: "bcb");
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([
            new SkuLabsItem("SB", "lB2", "1", "skb2", "bcb2", "B2"),
            new SkuLabsItem("SA", "lA2", "2", "ska2", "bca2", "A2")
        ]);

        var result = await CreateSut().Sync();

        var stored = await _dbContext.SkulabsItems.ToListAsync();
        stored.Count.ShouldBe(2);

        var newA = stored.Single(s => s.SkulabsSourceItemId == "SA");
        var newB = stored.Single(s => s.SkulabsSourceItemId == "SB");
        newA.ShopifyProductVariantId.ShouldBe(v2.ShopifyProductVariantId);
        newA.Title.ShouldBe("A2");
        newA.Sku.ShouldBe("ska2");
        newB.ShopifyProductVariantId.ShouldBe(v1.ShopifyProductVariantId);
        newB.Title.ShouldBe("B2");
        newB.Sku.ShouldBe("skb2");

        // Two link writes overall — distributed across Created/Updated depending on which
        // row was severed first. Total work done == 2.
        (result.CreatedSkulabsItemIds.Count + result.UpdatedSkulabsItemIds.Count).ShouldBe(2);
    }

    [Fact]
    public async Task Sync_ShouldFreeUniqueIndex_WhenSeveringDestinationVariantBeforeReLink()
    {
        // DB: V1↔SA, V2↔SB. API: V1↔SB (just one item, replacing V1's SA, and pulling SB off V2).
        // Without severing V1's old row first the re-link would conflict on the unique
        // index on ShopifyProductVariantId. SaveChanges must succeed.
        var v1 = SeedVariant(variantId: 1L);
        var v2 = SeedVariant(variantId: 2L);
        SeedSkulabsItem(v1.ShopifyProductVariantId, sourceItemId: "SA", sourceListingId: "la");
        var rowB = SeedSkulabsItem(v2.ShopifyProductVariantId, sourceItemId: "SB", sourceListingId: "lb");
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns([
            new SkuLabsItem("SB", "lb2", "1", "sku", "bar", "Title")
        ]);

        var result = await CreateSut().Sync();

        var stored = await _dbContext.SkulabsItems.ToListAsync();
        stored.Count.ShouldBe(1);
        stored[0].SkulabsItemId.ShouldBe(rowB.SkulabsItemId);
        stored[0].ShopifyProductVariantId.ShouldBe(v1.ShopifyProductVariantId);
        stored[0].SkulabsSourceItemId.ShouldBe("SB");

        result.UpdatedSkulabsItemIds.ShouldContain(rowB.SkulabsItemId);

        var v1Logs = await LogsForVariant(v1.ShopifyProductVariantId);
        v1Logs.Select(l => l.Message).ShouldBe([
            "Unlinked from SkuLabs item 'SA'.",
            "Linked to SkuLabs item 'SB'."
        ]);
        var v2Logs = await LogsForVariant(v2.ShopifyProductVariantId);
        v2Logs.Single().Message.ShouldBe("Unlinked from SkuLabs item 'SB'.");
    }

    // ---------- Helpers ----------

    private SkulabsItemSyncService CreateSut() => new(_skulabsClient, _dbContext, _logger);

    private async Task<List<ShopifyProductVariantLogEventEntity>> LogsForVariant(Guid variantGuid) =>
        await _dbContext.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid)
            .OrderBy(l => l.CreatedOn)
            .ThenBy(l => l.ShopifyProductVariantLogEventId)
            .ToListAsync();

    private ShopifyProductVariantEntity SeedVariant(
        long variantId = 200,
        string sku = "SKU",
        string barcode = "BAR")
    {
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = $"gid://shopify/Product/{variantId}",
            ProductId = variantId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = "Variant",
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

    private static SkuLabsItem NewSkulabsItem(
        string itemId = "src",
        string listingId = "lst",
        string variantId = "200",
        string sku = "sku",
        string barcode = "bar",
        string title = "Title") => new(itemId, listingId, variantId, sku, barcode, title);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
