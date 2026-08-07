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
        _skulabsClient.GetAllItems().Returns(Collection());
        var sut = CreateSut();

        var result = await sut.Sync();

        result.CreatedSkulabsItemIds.ShouldBeEmpty();
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();
        result.UnresolvedListingCount.ShouldBe(0);
        result.SkippedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Sync_ShouldNotWipeExistingItems_WhenSkulabsReturnsNothing()
    {
        // An empty payload means "the call told us nothing", not "SkuLabs has no items".
        SeedVariant(variantId: 200L);
        SeedSkulabsItem("src", StoredListing("lst", "200"));
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection());

        await CreateSut().Sync();

        (await _dbContext.SkulabsItems.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Sync_ShouldStoreItemWithUnresolvedListing_WhenNoMatchingVariantInDatabase()
    {
        _skulabsClient.GetAllItems().Returns(Collection(ApiItem("src", Listing("lst", "999"))));
        var sut = CreateSut();

        var result = await sut.Sync();

        result.UnresolvedListingCount.ShouldBe(1);
        result.CreatedSkulabsItemIds.Count.ShouldBe(1);

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.Single().ShopifyProductVariantId.ShouldBeNull();
        stored.Listings.Single().RawVariantId.ShouldBe("999");
    }

    [Fact]
    public async Task Sync_ShouldUseShopifyVariantIdAndNotSkuOrBarcode_ForMatching()
    {
        // Variant has matching SKU/barcode values but a *different* VariantId.
        // Must not resolve — resolution is by variant id only.
        SeedVariant(variantId: 999L, sku: "shared-sku", barcode: "shared-barcode");
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("src", "Title", "shared-sku", "shared-barcode", Listing("lst", "200"))));

        var result = await CreateSut().Sync();

        result.UnresolvedListingCount.ShouldBe(1);
        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.Single().ShopifyProductVariantId.ShouldBeNull();
    }

    // ---------- Creating a link ----------

    [Fact]
    public async Task Sync_ShouldCreateSkulabsItem_WhenMatchingVariantHasNoneInDatabase()
    {
        var variant = SeedVariant(variantId: 45696210862241L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem(
                "69b4543c6642ed434a5b1c4a",
                "Yellow Vintage Nature Domino Necklace (Goose (1bird))",
                "1 bird",
                "10862241",
                Listing("69b454b06642ed434a5bf571", "45696210862241"))));

        var result = await CreateSut().Sync();

        result.CreatedSkulabsItemIds.Count.ShouldBe(1);
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.SkulabsSourceItemId.ShouldBe("69b4543c6642ed434a5b1c4a");
        stored.Sku.ShouldBe("1 bird");
        stored.Barcode.ShouldBe("10862241");
        stored.Title.ShouldBe("Yellow Vintage Nature Domino Necklace (Goose (1bird))");

        var listing = stored.Listings.Single();
        listing.SkulabsSourceListingId.ShouldBe("69b454b06642ed434a5bf571");
        listing.ShopifyProductVariantId.ShouldBe(variant.ShopifyProductVariantId);
        result.CreatedSkulabsItemIds.ShouldContain(stored.SkulabsItemId);
    }

    [Fact]
    public async Task Sync_ShouldAddLinkedLogOnVariant_WhenCreatingNewLink()
    {
        var variant = SeedVariant(variantId: 200L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("skulabs-1", Listing("lst", "200"))));

        await CreateSut().Sync();

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Count.ShouldBe(1);
        logs[0].Message.ShouldBe("Linked to SkuLabs item 'skulabs-1'.");
    }

    // ---------- Unchanged link is a no-op ----------

    [Fact]
    public async Task Sync_ShouldBeNoOp_WhenLinkIsAlreadyIdentical()
    {
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("src-1", StoredListing("lst-1", "200", variant),
            title: "Same Title", sku: "same-sku", barcode: "same-bar");
        await _dbContext.SaveChangesAsync();
        var originalId = existing.SkulabsItemId;

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("src-1", "Same Title", "same-sku", "same-bar", Listing("lst-1", "200"))));

        var result = await CreateSut().Sync();

        result.CreatedSkulabsItemIds.ShouldBeEmpty();
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.SkulabsItemId.ShouldBe(originalId);
    }

    [Fact]
    public async Task Sync_ShouldNotRefreshMetadata_WhenLinkIsUnchangedButMetadataDiffers()
    {
        // Same SkuLabs source id, same listing, same variant — only title/sku/barcode differ.
        // The title is ours to push, so a stale SkuLabs value must not overwrite it here.
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("src-1", StoredListing("lst-1", "200", variant),
            title: "Old Title", sku: "old-sku", barcode: "old-bar");
        await _dbContext.SaveChangesAsync();
        var originalId = existing.SkulabsItemId;

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("src-1", "New Title", "new-sku", "new-bar", Listing("lst-1", "200"))));

        var result = await CreateSut().Sync();

        result.CreatedSkulabsItemIds.ShouldBeEmpty();
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.SkulabsItemId.ShouldBe(originalId);
        stored.Title.ShouldBe("Old Title");
        stored.Sku.ShouldBe("old-sku");
        stored.Barcode.ShouldBe("old-bar");
    }

    [Fact]
    public async Task Sync_ShouldRefreshLastSeen_WhenItemIsStillReported()
    {
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("src-1", StoredListing("lst-1", "200", variant));
        existing.LastSeenUtc = DateTime.UtcNow.AddDays(-3);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(ApiItem("src-1", Listing("lst-1", "200"))));

        await CreateSut().Sync();

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.LastSeenUtc.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
    }

    // ---------- Warehouse location, an inbound-only mirror ----------

    [Fact]
    public async Task Sync_ShouldStoreLocation_WhenItemIsCreated()
    {
        SeedVariant(variantId: 200L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItemAt("src-1", "A-01-06", Listing("lst-1", "200"))));

        await CreateSut().Sync();

        (await _dbContext.SkulabsItems.SingleAsync()).Location.ShouldBe("A-01-06");
    }

    [Fact]
    public async Task Sync_ShouldRefreshLocation_WhenLinkIsUnchangedAndLocationMovedUpstream()
    {
        // The rule this whole feature turns on. Merchants move bins, so the location follows SkuLabs
        // on every run — unlike the title, which is ours to push and stays put. And because we never
        // push a location, moving one owes SkuLabs nothing: the pending flag must stay clear.
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("src-1", StoredListing("lst-1", "200", variant),
            title: "Old Title", sku: "old-sku", barcode: "old-bar");
        existing.Location = "A-01-06";
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItemAt("src-1", "W-04-18", Listing("lst-1", "200"))));

        var result = await CreateSut().Sync();

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.Location.ShouldBe("W-04-18");
        stored.Title.ShouldBe("Old Title");
        stored.PendingSkulabsSync.ShouldBeFalse();

        // A location move is not a re-link, so it is not reported as one.
        result.UpdatedSkulabsItemIds.ShouldBeEmpty();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sync_ShouldClearLocation_WhenSkulabsStopsReportingOne()
    {
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("src-1", StoredListing("lst-1", "200", variant));
        existing.Location = "A-01-06";
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItemAt("src-1", "", Listing("lst-1", "200"))));

        await CreateSut().Sync();

        (await _dbContext.SkulabsItems.SingleAsync()).Location.ShouldBe("");
    }

    [Fact]
    public async Task Sync_ShouldPreserveStoredLocation_WhenNoWarehouseIsConfigured()
    {
        // Turning the warehouse off means "stop syncing locations", not "erase the ones you have".
        // The client reports null in that mode, which carries no opinion about the stored value.
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("src-1", StoredListing("lst-1", "200", variant));
        existing.Location = "A-01-06";
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItemAt("src-1", null, Listing("lst-1", "200"))));

        await CreateSut().Sync();

        (await _dbContext.SkulabsItems.SingleAsync()).Location.ShouldBe("A-01-06");
    }

    [Fact]
    public async Task Sync_ShouldStoreEmptyLocation_WhenItemIsCreatedWithNoWarehouseConfigured()
    {
        // Nothing stored to protect, and the column is non-nullable, so an unknown location lands
        // as empty rather than propagating null into the entity.
        SeedVariant(variantId: 200L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItemAt("src-1", null, Listing("lst-1", "200"))));

        await CreateSut().Sync();

        (await _dbContext.SkulabsItems.SingleAsync()).Location.ShouldBe("");
    }

    [Fact]
    public async Task Sync_ShouldRefreshLocation_WhenItemIsAmbiguous()
    {
        // Ambiguity is about which variant an item links to; the bin it sits in is known regardless.
        SeedVariant(variantId: 200L);
        SeedVariant(variantId: 201L);
        var existing = SeedSkulabsItem("src-1",
            StoredListing("lst-1", "200"), StoredListing("lst-2", "201"));
        existing.Location = "A-01-06";
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItemAt("src-1", "D-03-09", Listing("lst-1", "200"), Listing("lst-2", "201"))));

        await CreateSut().Sync();

        (await _dbContext.SkulabsItems.SingleAsync()).Location.ShouldBe("D-03-09");
    }

    [Fact]
    public async Task Sync_ShouldLeavePendingSkulabsSyncSet_WhenLocationChangesOnAnItemAlreadyOwedToSkulabs()
    {
        // An undispatched title correction must survive a location refresh — the two directions of
        // travel are independent.
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("src-1", StoredListing("lst-1", "200", variant),
            title: "Locally Corrected Title", sku: "sku", barcode: "bar");
        existing.Location = "A-01-06";
        existing.PendingSkulabsSync = true;
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItemAt("src-1", "W-04-18", Listing("lst-1", "200"))));

        await CreateSut().Sync();

        var stored = await _dbContext.SkulabsItems.SingleAsync();
        stored.Location.ShouldBe("W-04-18");
        stored.PendingSkulabsSync.ShouldBeTrue();
        stored.Title.ShouldBe("Locally Corrected Title");
    }

    // ---------- Re-linking ----------

    [Fact]
    public async Task Sync_ShouldReLink_WhenSkulabsItemMovesToDifferentVariant()
    {
        // DB: V1 ↔ S2.  API: V3 ↔ S2, same listing id. Expected: the row is re-pointed, PK preserved.
        var variantV1 = SeedVariant(variantId: 1L);
        var variantV3 = SeedVariant(variantId: 3L);
        var existing = SeedSkulabsItem("S2", StoredListing("L-1", "1", variantV1),
            title: "Old Title", sku: "old-sku", barcode: "old-bar");
        await _dbContext.SaveChangesAsync();
        var preservedRowId = existing.SkulabsItemId;

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("S2", "New Title", "new-sku", "new-bar", Listing("L-1", "3"))));

        var result = await CreateSut().Sync();

        result.UpdatedSkulabsItemIds.ShouldContain(preservedRowId);
        result.CreatedSkulabsItemIds.ShouldBeEmpty();

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.SkulabsItemId.ShouldBe(preservedRowId);
        stored.Listings.Single().ShopifyProductVariantId.ShouldBe(variantV3.ShopifyProductVariantId);
        // Metadata refreshed because a new link was written.
        stored.Title.ShouldBe("New Title");
        stored.Sku.ShouldBe("new-sku");
        stored.Barcode.ShouldBe("new-bar");

        (await LogsForVariant(variantV1.ShopifyProductVariantId)).Single()
            .Message.ShouldBe("Unlinked from SkuLabs item 'S2'.");
        (await LogsForVariant(variantV3.ShopifyProductVariantId)).Single()
            .Message.ShouldBe("Linked to SkuLabs item 'S2'.");
    }

    [Fact]
    public async Task Sync_ShouldReplaceLink_WhenVariantGetsDifferentSkulabsItem()
    {
        // DB: V200 ↔ S-old.  API: V200 ↔ S-new, and S-old is gone from SkuLabs entirely.
        var variant = SeedVariant(variantId: 200L);
        SeedSkulabsItem("S-old", StoredListing("lst-old", "200", variant),
            title: "Old", sku: "old-sku", barcode: "old-bar");
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("S-new", "New", "new-sku", "new-bar", Listing("lst-new", "200"))));

        var result = await CreateSut().Sync();

        result.CreatedSkulabsItemIds.Count.ShouldBe(1);
        result.RemovedCount.ShouldBe(1);

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.SkulabsSourceItemId.ShouldBe("S-new");
        stored.Listings.Single().ShopifyProductVariantId.ShouldBe(variant.ShopifyProductVariantId);
        stored.Title.ShouldBe("New");

        var logs = await LogsForVariant(variant.ShopifyProductVariantId);
        logs.Select(l => l.Message).ShouldBe([
            "Linked to SkuLabs item 'S-new'.",
            "Unlinked from SkuLabs item 'S-old'."
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task Sync_ShouldHandleSwap_WhenTwoSkulabsItemsExchangeVariants()
    {
        // DB:  V1↔SA,  V2↔SB.  API:  V1↔SB,  V2↔SA. Nothing blocks re-pointing both rows in
        // place any more — the link lives on the listing, not on a unique column.
        var v1 = SeedVariant(variantId: 1L);
        var v2 = SeedVariant(variantId: 2L);
        SeedSkulabsItem("SA", StoredListing("lA", "1", v1), title: "A", sku: "ska", barcode: "bca");
        SeedSkulabsItem("SB", StoredListing("lB", "2", v2), title: "B", sku: "skb", barcode: "bcb");
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("SB", "B2", "skb2", "bcb2", Listing("lB", "1")),
            ApiItem("SA", "A2", "ska2", "bca2", Listing("lA", "2"))));

        var result = await CreateSut().Sync();

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).ToListAsync();
        stored.Count.ShouldBe(2);

        var newA = stored.Single(s => s.SkulabsSourceItemId == "SA");
        var newB = stored.Single(s => s.SkulabsSourceItemId == "SB");
        newA.Listings.Single().ShopifyProductVariantId.ShouldBe(v2.ShopifyProductVariantId);
        newA.Title.ShouldBe("A2");
        newB.Listings.Single().ShopifyProductVariantId.ShouldBe(v1.ShopifyProductVariantId);
        newB.Title.ShouldBe("B2");

        result.UpdatedSkulabsItemIds.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Sync_ShouldRemoveItem_WhenSkulabsNoLongerReportsIt()
    {
        var variant = SeedVariant(variantId: 200L);
        SeedSkulabsItem("gone", StoredListing("lst", "200", variant));
        SeedSkulabsItem("kept", StoredListing("lst-2", "200"));
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(ApiItem("kept", Listing("lst-2", "999"))));

        var result = await CreateSut().Sync();

        result.RemovedCount.ShouldBe(1);
        (await _dbContext.SkulabsItems.SingleAsync()).SkulabsSourceItemId.ShouldBe("kept");
        (await LogsForVariant(variant.ShopifyProductVariantId)).Single()
            .Message.ShouldBe("Unlinked from SkuLabs item 'gone'.");
    }

    // ---------- Ambiguity, derived from listing cardinality ----------

    [Fact]
    public async Task Sync_ShouldKeepEveryListing_WhenItemHasMoreThanOne()
    {
        var variantA = SeedVariant(variantId: 10L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("multi", Listing("lst-a", "10"), Listing("lst-b", "20"))));

        var result = await CreateSut().Sync();

        result.AmbiguousCount.ShouldBe(1);

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.SkulabsSourceItemId.ShouldBe("multi");
        stored.Listings.Count.ShouldBe(2);

        // The listing whose raw variant matches a known variant is resolved; the other is not.
        stored.Listings.Single(l => l.RawVariantId == "10").ShopifyProductVariantId
            .ShouldBe(variantA.ShopifyProductVariantId);
        stored.Listings.Single(l => l.RawVariantId == "20").ShopifyProductVariantId
            .ShouldBeNull();

        // Nothing about an ambiguous item is syncable, even the listing that did resolve.
        (await _dbContext.SkulabsItemListings.Where(SkulabsItemLinks.IsSyncable).CountAsync())
            .ShouldBe(0);

        // The resolved variant is told why it has no SkuLabs item. Claiming it was "linked" would
        // contradict what every other read path reports for it.
        var logs = await LogsForVariant(variantA.ShopifyProductVariantId);
        logs.Single().Message.ShouldBe(
            "SkuLabs item 'multi' lists 2 Shopify variants, so it was not linked to this one. "
            + "Resolve the duplicate listings in SkuLabs.");
        logs.ShouldNotContain(l => l.Message.StartsWith("Linked to SkuLabs item"));
    }

    [Fact]
    public async Task Sync_ShouldNotRepeatTheAmbiguityNotice_WhenItemStaysAmbiguous()
    {
        var variant = SeedVariant(variantId: 10L);
        SeedSkulabsItem("multi", StoredListing("lst-a", "10", variant), StoredListing("lst-b", "20"));
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("multi", Listing("lst-a", "10"), Listing("lst-b", "20"))));

        await CreateSut().Sync();

        // Already ambiguous before the run, so there is no transition to report.
        (await LogsForVariant(variant.ShopifyProductVariantId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sync_ShouldStoreItem_WhenItHasNoShopifyListings()
    {
        _skulabsClient.GetAllItems().Returns(Collection(ApiItem("empty")));

        var result = await CreateSut().Sync();

        result.AmbiguousCount.ShouldBe(0);
        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sync_ShouldDropTheListing_WhenItemLosesItsLastShopifyListing()
    {
        var variant = SeedVariant(variantId: 200L);
        SeedSkulabsItem("gone", StoredListing("lst", "200", variant));
        await _dbContext.SaveChangesAsync();

        // SkuLabs still returns the item, but its only listing is now internal (non-numeric variant),
        // so after filtering it has no Shopify listing at all.
        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("gone", Listing("lst", "not-a-number"))));

        await CreateSut().Sync();

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.ShouldBeEmpty();

        (await LogsForVariant(variant.ShopifyProductVariantId)).Single()
            .Message.ShouldBe("Unlinked from SkuLabs item 'gone'.");
    }

    [Fact]
    public async Task Sync_ShouldBecomeSyncable_WhenAmbiguousItemDropsToOneListing()
    {
        // The whole point of one table: an item crossing the ambiguity line keeps its identity.
        var variant = SeedVariant(variantId: 200L);
        var existing = SeedSkulabsItem("was-ambiguous",
            StoredListing("lst-a", "200", variant),
            StoredListing("lst-b", "300"));
        await _dbContext.SaveChangesAsync();
        var preservedRowId = existing.SkulabsItemId;
        var firstSeen = existing.FirstSeenUtc;

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("was-ambiguous", Listing("lst-a", "200"))));

        var result = await CreateSut().Sync();

        result.AmbiguousCount.ShouldBe(0);

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.SkulabsItemId.ShouldBe(preservedRowId);
        stored.FirstSeenUtc.ShouldBe(firstSeen);
        stored.Listings.Single().ShopifyProductVariantId.ShouldBe(variant.ShopifyProductVariantId);

        (await _dbContext.SkulabsItemListings.Where(SkulabsItemLinks.IsSyncable).CountAsync())
            .ShouldBe(1);

        // Now that the ambiguity is resolved the variant really is linked, and says so.
        (await LogsForVariant(variant.ShopifyProductVariantId)).Single()
            .Message.ShouldBe("Linked to SkuLabs item 'was-ambiguous'.");
    }

    [Fact]
    public async Task Sync_ShouldStopBeingSyncable_WhenSyncedItemGainsASecondListing()
    {
        var variant = SeedVariant(variantId: 200L);
        SeedSkulabsItem("now-ambiguous", StoredListing("lst-a", "200", variant));
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("now-ambiguous", Listing("lst-a", "200"), Listing("lst-b", "300"))));

        var result = await CreateSut().Sync();

        result.AmbiguousCount.ShouldBe(1);

        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.Count.ShouldBe(2);
        (await _dbContext.SkulabsItemListings.Where(SkulabsItemLinks.IsSyncable).CountAsync())
            .ShouldBe(0);

        // The variant genuinely lost a link it had, so it is told both that and why.
        (await LogsForVariant(variant.ShopifyProductVariantId)).Select(l => l.Message).ShouldBe([
            "Unlinked from SkuLabs item 'now-ambiguous'.",
            "SkuLabs item 'now-ambiguous' lists 2 Shopify variants, so it was not linked to this one. "
            + "Resolve the duplicate listings in SkuLabs."
        ]);
    }

    [Fact]
    public async Task Sync_ShouldNotMakeEitherLinkSyncable_WhenTwoItemsClaimTheSameVariant()
    {
        // The variant half of the cardinality guard. Both items have exactly one listing, so each
        // looks syncable on its own — only the contested variant disqualifies them.
        SeedVariant(variantId: 200L);
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("first", Listing("lst-1", "200")),
            ApiItem("second", Listing("lst-2", "200"))));

        await CreateSut().Sync();

        (await _dbContext.SkulabsItems.CountAsync()).ShouldBe(2);
        (await _dbContext.SkulabsItemListings.CountAsync()).ShouldBe(2);
        (await _dbContext.SkulabsItemListings.Where(SkulabsItemLinks.IsSyncable).CountAsync())
            .ShouldBe(0);
    }

    [Fact]
    public async Task Sync_ShouldStillResolveDeletedVariant_OnAmbiguousItemListing()
    {
        // A deleted Shopify variant is treated as "gone" for syncing, but a multi-listing item that
        // still references it must surface the deleted variant so it can be fixed in SkuLabs. The
        // variant lookup deliberately includes deleted variants for this reason.
        var liveVariant = SeedVariant(variantId: 10L);
        var deletedVariant = SeedVariant(variantId: 20L);
        deletedVariant.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("multi", Listing("lst-a", "10"), Listing("lst-b", "20"))));

        var result = await CreateSut().Sync();

        result.AmbiguousCount.ShouldBe(1);
        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.Single(l => l.RawVariantId == "10").ShopifyProductVariantId
            .ShouldBe(liveVariant.ShopifyProductVariantId);
        stored.Listings.Single(l => l.RawVariantId == "20").ShopifyProductVariantId
            .ShouldBe(deletedVariant.ShopifyProductVariantId);
    }

    [Fact]
    public async Task Sync_ShouldLinkToDeletedVariant_WhenSingleListingResolvesToOne()
    {
        var deletedVariant = SeedVariant(variantId: 200L);
        deletedVariant.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(ApiItem("single", Listing("lst", "200"))));

        var result = await CreateSut().Sync();

        // Mapped once and then invisible on the item-sync page via that endpoint's IsDeleted filter.
        result.CreatedSkulabsItemIds.Count.ShouldBe(1);
        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.Single().ShopifyProductVariantId.ShouldBe(deletedVariant.ShopifyProductVariantId);
    }

    [Fact]
    public async Task Sync_ShouldReplaceListings_WhenItemRemainsAmbiguousWithDifferentOnes()
    {
        SeedSkulabsItem("multi", StoredListing("lst-1", "1"), StoredListing("lst-2", "2"));
        await _dbContext.SaveChangesAsync();

        _skulabsClient.GetAllItems().Returns(Collection(
            ApiItem("multi", Listing("lst-x", "3"), Listing("lst-y", "4"), Listing("lst-z", "5"))));

        var result = await CreateSut().Sync();

        result.AmbiguousCount.ShouldBe(1);
        var stored = await _dbContext.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.Listings.Select(l => l.RawVariantId).OrderBy(x => x).ShouldBe(["3", "4", "5"]);
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
        string sourceItemId,
        params SkulabsItemListingEntity[] listings) =>
        SeedSkulabsItem(sourceItemId, "Title", "sku", "bar", listings);

    private SkulabsItemEntity SeedSkulabsItem(
        string sourceItemId,
        SkulabsItemListingEntity listing,
        string title = "Title",
        string sku = "sku",
        string barcode = "bar") =>
        SeedSkulabsItem(sourceItemId, title, sku, barcode, [listing]);

    private SkulabsItemEntity SeedSkulabsItem(
        string sourceItemId,
        string title,
        string sku,
        string barcode,
        SkulabsItemListingEntity[] listings)
    {
        var entity = new SkulabsItemEntity
        {
            SkulabsItemId = Guid.NewGuid(),
            SkulabsSourceItemId = sourceItemId,
            Title = title,
            Sku = sku,
            Barcode = barcode
        };

        foreach (var listing in listings)
        {
            entity.Listings.Add(listing);
        }

        _dbContext.SkulabsItems.Add(entity);
        return entity;
    }

    /// <summary>A stored listing row, optionally already resolved to a seeded variant.</summary>
    private static SkulabsItemListingEntity StoredListing(
        string listingId,
        string rawVariantId,
        ShopifyProductVariantEntity? variant = null) =>
        new()
        {
            SkulabsItemListingId = Guid.NewGuid(),
            SkulabsSourceListingId = listingId,
            RawVariantId = rawVariantId,
            ShopifyProductId = "prod",
            ShopifyProductVariantId = variant?.ShopifyProductVariantId
        };

    private static SkulabsItemCollection Collection(params SkulabsApiItem[] items) => new(items);

    private static SkulabsApiItem ApiItem(string itemId, params SkulabsApiListing[] listings) =>
        new(itemId, "Name", "sku", "upc", "", listings);

    private static SkulabsApiItem ApiItem(
        string itemId,
        string name,
        string sku,
        string upc,
        params SkulabsApiListing[] listings) =>
        new(itemId, name, sku, upc, "", listings);

    /// <summary>An item whose location is known ("" for none) or unknown (null, warehouse unset).</summary>
    private static SkulabsApiItem ApiItemAt(
        string itemId,
        string? location,
        params SkulabsApiListing[] listings) =>
        new(itemId, "Name", "sku", "upc", location, listings);

    private static SkulabsApiListing Listing(string listingId, string rawVariantId) =>
        new(listingId, rawVariantId, "prod");

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
