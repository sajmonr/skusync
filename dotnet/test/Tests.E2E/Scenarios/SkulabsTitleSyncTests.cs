using Application.Jobs;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Tests.E2E.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class SkulabsTitleSyncTests(AppServerTestHost factory) : IAsyncLifetime
{
    private const long LinkedVariantId = 46450996871329L;
    private const string SkulabsSourceItemId = "title-sync-src-1";

    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Webhook ingest → inline reconcile → scheduled SkuLabs dispatch ----------

    [Fact]
    public async Task ProductsUpdateWebhook_MarksItemPending_AndDispatchPushesTitleToSkulabs()
    {
        // Seed a variant + linked SkuLabs item whose titles already match each other but will
        // diverge once the inbound webhook applies the Shopify product title to DisplayName.
        // The fixture's product title is "Testprod1" (Default Title variant → composed = "Testprod1").
        const long productId = 8521775284385;
        var variantGuid = await SeedLinkedVariantAsync(
            productId: productId,
            variantId: LinkedVariantId,
            variantDisplayName: "Stale Title",
            skulabsTitle: "Stale Title",
            skulabsSourceItemId: SkulabsSourceItemId);

        StubBulkUpsertOk();

        factory.ShopifyGraphQl
            .ExecuteAsync<UpdateVariantsGraphResponse>(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>>())
            .Returns(new UpdateVariantsGraphResponse(null));

        var envelope = await FixtureLoader.LoadAsync<SqsShopEventProductMessage>(
            "Shopify/Webhooks/products-update-single-variant.json");

        await factory.DispatchWebhookAsync(envelope);

        // The inline reconcile mirrors the new title into the item and marks it pending — the
        // SkuLabs push itself rides the dispatch cadence, so no PUT has happened yet.
        using (var afterIngest = factory.Services.CreateScope())
        {
            var db = afterIngest.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var item = await db.SkulabsItems.SingleAsync();
            item.Title.ShouldBe("Testprod1");
            item.PendingSkulabsSync.ShouldBeTrue();
        }
        CapturedBulkUpsertBodies().ShouldBeEmpty();

        // The scheduled dispatch drains the pending item to SkuLabs and clears the flag.
        await RunSkulabsDispatchAsync();

        var bodies = CapturedBulkUpsertBodies();
        bodies.Count.ShouldBe(1);
        bodies[0].ShouldContain($"\"_id\":\"{SkulabsSourceItemId}\"");
        bodies[0].ShouldContain("\"name\":\"Testprod1\"");

        using var scope = factory.Services.CreateScope();
        var dbAfter = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedItem = await dbAfter.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Testprod1");
        storedItem.PendingSkulabsSync.ShouldBeFalse();

        var titleLog = await dbAfter.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid
                        && l.Message.Contains("SkuLabs item title corrected"))
            .SingleAsync();
        titleLog.Message.ShouldBe(
            "SkuLabs item title corrected to match variant: 'Stale Title' → 'Testprod1'.");
    }

    // ---------- Item-sync ingest → inline reconcile → scheduled SkuLabs dispatch ----------

    [Fact]
    public async Task SkulabsSyncJob_MarksNewlyLinkedItemPending_AndDispatchPushesVariantDisplayName()
    {
        // Seed a variant whose DisplayName we want to keep ("Authoritative Display Name").
        // The /item/get fixture's name field ("Yellow Vintage Nature Domino Necklace (Goose (1bird))")
        // lands in SkulabsItem.Title on link, immediately diverging from the variant — the item
        // sync's inline reconcile mirrors the variant value and marks the item pending.
        const long variantId = 45696210862241L;
        var variantGuid = await SeedVariantAsync(variantId, displayName: "Authoritative Display Name");

        StubSkulabsItemGet("Skulabs/Api/items-get-single.json");
        StubBulkUpsertOk();

        await RunSkulabsItemSyncJobAsync();

        using (var afterIngest = factory.Services.CreateScope())
        {
            var db = afterIngest.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var item = await db.SkulabsItems.SingleAsync();
            item.Title.ShouldBe("Authoritative Display Name");
            item.PendingSkulabsSync.ShouldBeTrue();
        }
        CapturedBulkUpsertBodies().ShouldBeEmpty();

        await RunSkulabsDispatchAsync();

        var bodies = CapturedBulkUpsertBodies();
        bodies.Count.ShouldBe(1);
        bodies[0].ShouldContain("\"_id\":\"69b4543c6642ed434a5b1c4a\"");
        bodies[0].ShouldContain("\"name\":\"Authoritative Display Name\"");

        using var scope = factory.Services.CreateScope();
        var dbAfter = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedItem = await dbAfter.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Authoritative Display Name");
        storedItem.PendingSkulabsSync.ShouldBeFalse();

        var titleLogs = await dbAfter.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid
                        && l.Message.Contains("SkuLabs item title corrected"))
            .ToListAsync();
        titleLogs.Count.ShouldBe(1);
        titleLogs[0].Message.ShouldBe(
            "SkuLabs item title corrected to match variant: " +
            "'Yellow Vintage Nature Domino Necklace (Goose (1bird))' → 'Authoritative Display Name'.");
    }

    // ---------- Nightly reconcile as the safety net ----------

    [Fact]
    public async Task FullReconcileAndDispatch_PushCorrections_WhenDatabaseAlreadyHasDriftedTitles()
    {
        // Pre-existing drift in the DB that no ingest ever reconciled (e.g. manual edit or a
        // missed webhook). The nightly reconcile should detect and mark it; the dispatch drains it.
        const long productId = 9999000000001;
        var variantGuid = await SeedLinkedVariantAsync(
            productId: productId,
            variantId: 9999000000002L,
            variantDisplayName: "Newest Authoritative Title",
            skulabsTitle: "Forgotten Old Title",
            skulabsSourceItemId: "maintenance-sweep-src");

        StubBulkUpsertOk();

        using var scope = factory.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();

        await recurringJobs.ReconcileAll(CancellationToken.None);
        await recurringJobs.DispatchSkulabs(CancellationToken.None);

        var bodies = CapturedBulkUpsertBodies();
        bodies.Count.ShouldBe(1);
        bodies[0].ShouldContain("\"_id\":\"maintenance-sweep-src\"");
        bodies[0].ShouldContain("\"name\":\"Newest Authoritative Title\"");

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedItem = await db.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Newest Authoritative Title");
        storedItem.PendingSkulabsSync.ShouldBeFalse();

        var titleLog = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid
                        && l.Message.Contains("SkuLabs item title corrected"))
            .SingleAsync();
        titleLog.Message.ShouldBe(
            "SkuLabs item title corrected to match variant: " +
            "'Forgotten Old Title' → 'Newest Authoritative Title'.");
    }

    [Fact]
    public async Task FullReconcileAndDispatch_DoNotCallSkulabs_WhenAllTitlesAlreadyMatch()
    {
        await SeedLinkedVariantAsync(
            productId: 9999000000010L,
            variantId: 9999000000011L,
            variantDisplayName: "In Sync",
            skulabsTitle: "In Sync",
            skulabsSourceItemId: "in-sync-src");

        StubBulkUpsertOk();

        using var scope = factory.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();

        await recurringJobs.ReconcileAll(CancellationToken.None);
        await recurringJobs.DispatchSkulabs(CancellationToken.None);

        CapturedBulkUpsertBodies().ShouldBeEmpty();
    }

    // ---------- Helpers ----------

    private async Task RunSkulabsItemSyncJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();
        await recurringJobs.SyncSkulabsItems(CancellationToken.None);
    }

    private async Task RunSkulabsDispatchAsync()
    {
        using var scope = factory.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();
        await recurringJobs.DispatchSkulabs(CancellationToken.None);
    }

    private void StubBulkUpsertOk() =>
        factory.WireMock
            .Given(Request.Create().WithPath("/item/bulk_upsert").UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"success":true}"""));

    private void StubSkulabsItemGet(string fixtureRelativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureRelativePath);
        var json = File.ReadAllText(fullPath);

        factory.WireMock
            .Given(Request.Create().WithPath("/item/get").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(json));
    }

    private List<string> CapturedBulkUpsertBodies() =>
        factory.WireMock.LogEntries
            .Where(e => e.RequestMessage is { } req
                        && string.Equals(req.Method, "PUT", StringComparison.OrdinalIgnoreCase)
                        && (req.Path?.EndsWith("/item/bulk_upsert", StringComparison.Ordinal) ?? false))
            .Select(e => e.RequestMessage!.Body ?? "")
            .ToList();

    private async Task<Guid> SeedVariantAsync(long variantId, string displayName)
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
            Sku = "seed-sku",
            Barcode = "seed-barcode"
        };
        db.ShopifyProductVariants.Add(entity);
        await db.SaveChangesAsync();
        return entity.ShopifyProductVariantId;
    }

    private async Task<Guid> SeedLinkedVariantAsync(
        long productId,
        long variantId,
        string variantDisplayName,
        string skulabsTitle,
        string skulabsSourceItemId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variant = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.CreateVersion7(),
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = variantDisplayName,
            Sku = $"sku-{variantId}",
            Barcode = $"bar-{variantId}"
        };
        db.ShopifyProductVariants.Add(variant);

        db.SkulabsItems.Add(new SkulabsItemEntity
        {
            SkulabsItemId = Guid.CreateVersion7(),
            SkulabsSourceItemId = skulabsSourceItemId,
            Title = skulabsTitle,
            Sku = $"sku-{variantId}",
            Barcode = $"bar-{variantId}",
            Listings =
            {
                new SkulabsItemListingEntity
                {
                    SkulabsSourceListingId = $"listing-{skulabsSourceItemId}",
                    RawVariantId = variantId.ToString(),
                    ShopifyProductId = productId.ToString(),
                    ShopifyProductVariantId = variant.ShopifyProductVariantId
                }
            }
        });

        await db.SaveChangesAsync();
        return variant.ShopifyProductVariantId;
    }
}
