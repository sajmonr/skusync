using Application.Jobs;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tests.E2E.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class SkulabsItemSyncTests(AppServerTestHost factory) : IAsyncLifetime
{
    private const long MatchingVariantId = 45696210862241L;

    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SkulabsSyncJob_CreatesSkulabsItem_WhenMatchingVariantExistsAndNoneInDatabase()
    {
        // Seed the variant already matching the fixture's SKU/barcode/title so the item sync's
        // inline reconcile is a no-op — this test is about ID-based linking, not reconciliation.
        var variantGuid = await SeedVariantAsync(
            MatchingVariantId,
            displayName: "Yellow Vintage Nature Domino Necklace (Goose (1bird))",
            sku: "1 bird",
            barcode: "10862241");
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-single.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.SkulabsSourceItemId.ShouldBe("69b4543c6642ed434a5b1c4a");
        stored.Listings.Single().ShopifyProductVariantId.ShouldBe(variantGuid);
        stored.Listings.Single().SkulabsSourceListingId.ShouldBe("69b454b06642ed434a5bf571");
        stored.Sku.ShouldBe("1 bird");
        stored.Barcode.ShouldBe("10862241");
        stored.Title.ShouldBe("Yellow Vintage Nature Domino Necklace (Goose (1bird))");
        // Resolved out of the fixture's alias_locations map by the configured warehouse id.
        stored.Location.ShouldBe("A-01-06");

        var logs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid)
            .ToListAsync();
        // Two: the link itself, and the bin location the item reports — which is now a decided
        // field rather than a passively mirrored one, so acquiring it is a recorded change.
        logs.Count.ShouldBe(2);
        logs.Select(l => l.Message).ShouldContain("Linked to SkuLabs item '69b4543c6642ed434a5b1c4a'.");
        logs.Select(l => l.Message).ShouldContain("Location changed from '' to 'A-01-06'.");

        // Nothing drifted, so the inline reconcile marked nothing pending.
        (await db.ShopifyProductVariants.SingleAsync()).PendingShopifySync.ShouldBeFalse();
        stored.PendingSkulabsSync.ShouldBeFalse();
    }

    [Fact]
    public async Task SkulabsSyncJob_KeepsTheSameRow_AndRefreshesItsMetadata_WhenTheLinkIsUnchanged()
    {
        // Same SkuLabs source item id, same variant — metadata diverges between DB and API.
        // The link is still decided by ids alone, so the row keeps its identity; the fields are a
        // record of what SkuLabs last said and are overwritten regardless. They used to be left
        // alone, because the row also carried the value we intended to push.
        var variantGuid = await SeedVariantAsync(MatchingVariantId);
        var existingItemId = await SeedSkulabsItemAsync(
            variantGuid,
            sourceItemId: "69b4543c6642ed434a5b1c4a",
            sourceListingId: "69b454b06642ed434a5bf571",
            title: "Old Title",
            sku: "old-sku",
            barcode: "old-barcode");
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-single.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.SkulabsItems.Include(i => i.Listings).SingleAsync();
        // Same row — the primary key is preserved — carrying what SkuLabs now reports.
        stored.SkulabsItemId.ShouldBe(existingItemId);
        stored.Listings.Single().ShopifyProductVariantId.ShouldBe(variantGuid);
        stored.Title.ShouldBe("Yellow Vintage Nature Domino Necklace (Goose (1bird))");
        stored.Sku.ShouldBe("1 bird");
        stored.Barcode.ShouldBe("10862241");
        stored.Location.ShouldBe("A-01-06");

        // And the codes it reports are adopted, because a SkuLabs code may already be on a label.
        var desired = await db.DesiredItemStates.SingleAsync();
        desired.Sku.ShouldBe("1 bird");
        desired.Barcode.ShouldBe("10862241");
    }

    [Fact]
    public async Task SkulabsSyncJob_RefreshesLocation_WhenItMovesUpstreamOnAnAlreadyLinkedItem()
    {
        // Same link, same everything — only the bin moved. Nothing about the link changed, and the
        // mirror still picks it up, because the mirror records what SkuLabs said rather than what
        // the link did.
        var variantGuid = await SeedVariantAsync(MatchingVariantId);
        await SeedSkulabsItemAsync(
            variantGuid,
            sourceItemId: "69b4543c6642ed434a5b1c4a",
            sourceListingId: "69b454b06642ed434a5bf571",
            title: "Old Title",
            sku: "old-sku",
            barcode: "old-barcode",
            location: "B-07-14");
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-single.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.SkulabsItems.SingleAsync();
        stored.Location.ShouldBe("A-01-06");
        (await db.DesiredItemStates.SingleAsync()).Location.ShouldBe("A-01-06");
    }

    [Fact]
    public async Task SkulabsSyncJob_RelinksToNewVariant_WhenSkulabsItemMovesVariants()
    {
        // DB:  oldVariant ↔ skulabs item S.
        // API: skulabs item S now points at newVariant (99999999999999).
        // Expected: link severed from oldVariant, established on newVariant; metadata refreshed.
        var oldVariantGuid = await SeedVariantAsync(MatchingVariantId);
        // Seed the new variant already matching the relinked fixture's values so the post-link
        // inline reconcile is a no-op (it would otherwise mirror + mark pending, which is not
        // what this linking-contract test is about).
        var newVariantGuid = await SeedVariantAsync(
            99999999999999L,
            displayName: "Yellow Vintage Nature Domino Necklace (Goose (1bird))",
            sku: "1 bird",
            barcode: "10862241");
        var rowId = await SeedSkulabsItemAsync(
            oldVariantGuid,
            sourceItemId: "69b4543c6642ed434a5b1c4a",
            sourceListingId: "69b454b06642ed434a5bf571",
            title: "Old Title",
            sku: "old-sku",
            barcode: "old-barcode");
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-relinked-variant.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.SkulabsItems.Include(i => i.Listings).SingleAsync();
        // Same PK — the row was re-pointed, not deleted + recreated.
        stored.SkulabsItemId.ShouldBe(rowId);
        stored.Listings.Single().ShopifyProductVariantId.ShouldBe(newVariantGuid);
        // Metadata refreshed from the API payload because a new link was written.
        stored.Title.ShouldBe("Yellow Vintage Nature Domino Necklace (Goose (1bird))");
        stored.Sku.ShouldBe("1 bird");
        stored.Barcode.ShouldBe("10862241");
        // This fixture's alias_locations names a different warehouse, so we hold no location for it.
        stored.Location.ShouldBe("");

        var oldLogs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == oldVariantGuid)
            .ToListAsync();
        oldLogs.Single().Message.ShouldBe("Unlinked from SkuLabs item '69b4543c6642ed434a5b1c4a'.");

        var newLogs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == newVariantGuid)
            .ToListAsync();
        newLogs.ShouldContain(l => l.Message == "Linked to SkuLabs item '69b4543c6642ed434a5b1c4a'.");
    }

    [Fact]
    public async Task SkulabsSyncJob_StoresItemWithUnresolvedListing_WhenNoMatchingVariantExists()
    {
        // No variant seeded with VariantId 45696210862241. The item is still mirrored — the listing
        // simply resolves to nothing, which is what makes the gap visible.
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-single.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.SkulabsItems.Include(i => i.Listings).SingleAsync();
        var listing = stored.Listings.Single();
        listing.ShopifyProductVariantId.ShouldBeNull();
        listing.RawVariantId.ShouldBe(MatchingVariantId.ToString());
    }

    [Fact]
    public async Task SkulabsSyncJob_StoresItemAsAmbiguous_WhenItHasMultipleListings()
    {
        // The item has two listings — one pointing at our seeded variant, one that isn't ours.
        var variantGuid = await SeedVariantAsync(MatchingVariantId);
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-multiple-listings.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // One item row carrying both listings; the matching listing resolves to our variant.
        var stored = await db.SkulabsItems.Include(i => i.Listings).SingleAsync();
        stored.SkulabsSourceItemId.ShouldBe("ambiguous-multi-item");
        stored.Listings.Count.ShouldBe(2);
        // An item nobody can link to still sits somewhere, and we still mirror where.
        stored.Location.ShouldBe("D-03-09");

        stored.Listings.Single(l => l.RawVariantId == MatchingVariantId.ToString())
            .ShopifyProductVariantId.ShouldBe(variantGuid);
        stored.Listings.Single(l => l.RawVariantId != MatchingVariantId.ToString())
            .ShopifyProductVariantId.ShouldBeNull();

        // Ambiguity is derived, not stored: the item is present but no link passes the guard, so
        // nothing about it is syncable.
        (await db.SkulabsItemListings.Where(SkulabsItemLinks.IsSyncable).CountAsync()).ShouldBe(0);

        // The variant's history says it was not linked, and why — never that it was.
        var logs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid)
            .ToListAsync();
        logs.Single().Message.ShouldBe(
            "SkuLabs item 'ambiguous-multi-item' lists 2 Shopify variants, so it was not linked to "
            + "this one. Resolve the duplicate listings in SkuLabs.");
    }

    private async Task RunSyncJobAsync()
    {
        // Invoke the recurring-job entry point directly (rather than waiting for the Hangfire
        // schedule) so the test exercises the real sync service, real SkuLabs HTTP client
        // (hitting WireMock), real DbContext, and the inline reconcile end-to-end.
        using var scope = factory.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();
        await recurringJobs.SyncSkulabsItems(CancellationToken.None);
    }

    private async Task StubSkulabsGetAllAsync(string fixtureRelativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureRelativePath);
        var json = await File.ReadAllTextAsync(fullPath);

        factory.WireMock
            .Given(Request.Create().WithPath("/item/get").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(json));
    }

    private async Task<Guid> SeedVariantAsync(
        long variantId,
        string displayName = "Test Variant",
        string sku = "seed-sku",
        string barcode = "seed-barcode")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.CreateVersion7(),
            GlobalProductId = $"gid://shopify/Product/{variantId}",
            ProductId = variantId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = displayName,
            Sku = sku,
            Barcode = barcode
        };
        db.ShopifyProductVariants.Add(entity);
        // Post-migration every variant carries one, seeded from its own values. Without it the
        // reconciler treats the variant as never decided and re-derives its codes from scratch.
        db.DesiredItemStates.Add(new DesiredItemStateEntity
        {
            ShopifyProductVariantId = entity.ShopifyProductVariantId,
            Sku = entity.Sku,
            Barcode = entity.Barcode,
            Title = entity.DisplayName
        });
        await db.SaveChangesAsync();
        return entity.ShopifyProductVariantId;
    }

    private async Task<Guid> SeedSkulabsItemAsync(
        Guid variantGuid,
        string sourceItemId,
        string sourceListingId,
        string title,
        string sku,
        string barcode,
        string location = "")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entity = new SkulabsItemEntity
        {
            SkulabsItemId = Guid.CreateVersion7(),
            SkulabsSourceItemId = sourceItemId,
            Title = title,
            Sku = sku,
            Barcode = barcode,
            Location = location,
            Listings =
            {
                new SkulabsItemListingEntity
                {
                    SkulabsSourceListingId = sourceListingId,
                    RawVariantId = MatchingVariantId.ToString(),
                    ShopifyProductId = "8407892623521",
                    ShopifyProductVariantId = variantGuid
                }
            }
        };
        db.SkulabsItems.Add(entity);
        await db.SaveChangesAsync();
        return entity.SkulabsItemId;
    }
}
