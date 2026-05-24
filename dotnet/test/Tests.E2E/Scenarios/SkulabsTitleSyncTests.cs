using Application.Jobs.Maintenance;
using Application.Skulabs.Jobs;
using Application.Skulabs.Maintenance;
using Application.Skulabs.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using NSubstitute;
using Quartz;
using Shouldly;
using SlimMessageBus;
using Tests.E2E.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class SkulabsTitleSyncTests(E2EWebApplicationFactory factory) : IAsyncLifetime
{
    private const long LinkedVariantId = 46450996871329L;
    private const string SkulabsSourceItemId = "title-sync-src-1";

    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Event-driven push: variant title changed via webhook ----------

    [Fact]
    public async Task ProductsUpdateWebhook_PushesVariantTitleToSkulabs_WhenLinkedItemTitleDiverges()
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

        await AsyncWait.UntilAsync(
            () => CapturedBulkUpsertBodies().Any(),
            because: "SkulabsTitleSyncConsumer should have run and called PUT /item/bulk_upsert.");

        var bodies = CapturedBulkUpsertBodies();
        bodies.Count.ShouldBe(1);
        bodies[0].ShouldContain($"\"_id\":\"{SkulabsSourceItemId}\"");
        bodies[0].ShouldContain("\"name\":\"Testprod1\"");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedItem = await db.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Testprod1");

        var titleLog = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid
                        && l.Message.Contains("SkuLabs item title corrected"))
            .SingleAsync();
        titleLog.Message.ShouldBe(
            "SkuLabs item title corrected to match variant: 'Stale Title' → 'Testprod1'.");
    }

    // ---------- Event-driven push: SkuLabs item linked via sync job ----------

    [Fact]
    public async Task SkulabsSyncJob_PushesVariantDisplayName_WhenNewlyLinkedItemTitleDiffers()
    {
        // Seed a variant whose DisplayName we want to keep ("Authoritative Display Name").
        // The /item/get fixture's name field ("Yellow Vintage Nature Domino Necklace (Goose (1bird))")
        // will land in SkulabsItem.Title on link, immediately diverging from the variant — and the
        // post-link SkulabsTitleSyncConsumer should then push the variant value back up.
        const long variantId = 45696210862241L;
        var variantGuid = await SeedVariantAsync(variantId, displayName: "Authoritative Display Name");

        StubSkulabsItemGet("Skulabs/Api/items-get-single.json");
        StubBulkUpsertOk();

        await RunSkulabsItemSyncJobAsync();

        await AsyncWait.UntilAsync(
            () => CapturedBulkUpsertBodies().Any(),
            because: "Post-link SkulabsTitleSyncConsumer should have pushed the variant DisplayName to SkuLabs.");

        var bodies = CapturedBulkUpsertBodies();
        bodies.Count.ShouldBe(1);
        bodies[0].ShouldContain("\"_id\":\"69b4543c6642ed434a5b1c4a\"");
        bodies[0].ShouldContain("\"name\":\"Authoritative Display Name\"");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedItem = await db.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Authoritative Display Name");

        var titleLogs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid
                        && l.Message.Contains("SkuLabs item title corrected"))
            .ToListAsync();
        titleLogs.Count.ShouldBe(1);
        titleLogs[0].Message.ShouldBe(
            "SkuLabs item title corrected to match variant: " +
            "'Yellow Vintage Nature Domino Necklace (Goose (1bird))' → 'Authoritative Display Name'.");
    }

    // ---------- Maintenance task: drift sweep ----------

    [Fact]
    public async Task SkulabsTitleSyncTask_PushesCorrections_WhenDatabaseAlreadyHasDriftedTitles()
    {
        // Pre-existing drift in the DB that no event ever fired for (e.g. manual edit or a
        // missed message). The nightly maintenance task should detect and correct it.
        const long productId = 9999000000001;
        var variantGuid = await SeedLinkedVariantAsync(
            productId: productId,
            variantId: 9999000000002L,
            variantDisplayName: "Newest Authoritative Title",
            skulabsTitle: "Forgotten Old Title",
            skulabsSourceItemId: "maintenance-sweep-src");

        StubBulkUpsertOk();

        using var scope = factory.Services.CreateScope();
        var task = scope.ServiceProvider
            .GetServices<IMaintenanceTask>()
            .OfType<SkulabsTitleSyncTask>()
            .Single();

        await task.Execute(CancellationToken.None);

        var bodies = CapturedBulkUpsertBodies();
        bodies.Count.ShouldBe(1);
        bodies[0].ShouldContain("\"_id\":\"maintenance-sweep-src\"");
        bodies[0].ShouldContain("\"name\":\"Newest Authoritative Title\"");

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedItem = await db.SkulabsItems.SingleAsync();
        storedItem.Title.ShouldBe("Newest Authoritative Title");

        var titleLog = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid
                        && l.Message.Contains("SkuLabs item title corrected"))
            .SingleAsync();
        titleLog.Message.ShouldBe(
            "SkuLabs item title corrected to match variant: " +
            "'Forgotten Old Title' → 'Newest Authoritative Title'.");
    }

    [Fact]
    public async Task SkulabsTitleSyncTask_DoesNotCallSkulabs_WhenAllTitlesAlreadyMatch()
    {
        await SeedLinkedVariantAsync(
            productId: 9999000000010L,
            variantId: 9999000000011L,
            variantDisplayName: "In Sync",
            skulabsTitle: "In Sync",
            skulabsSourceItemId: "in-sync-src");

        StubBulkUpsertOk();

        using var scope = factory.Services.CreateScope();
        var task = scope.ServiceProvider
            .GetServices<IMaintenanceTask>()
            .OfType<SkulabsTitleSyncTask>()
            .Single();

        await task.Execute(CancellationToken.None);

        CapturedBulkUpsertBodies().ShouldBeEmpty();
    }

    // ---------- Helpers ----------

    private async Task RunSkulabsItemSyncJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<ISkulabsItemSyncService>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SkulabsItemSyncJob>>();
        var job = new SkulabsItemSyncJob(syncService, messageBus, featureManager, logger);

        var context = Substitute.For<IJobExecutionContext>();
        var trigger = Substitute.For<ITrigger>();
        trigger.Key.Returns(new TriggerKey("e2e-title-sync"));
        context.Trigger.Returns(trigger);
        context.CancellationToken.Returns(CancellationToken.None);
        context.FireTimeUtc.Returns(DateTimeOffset.UtcNow);

        await job.Execute(context);
    }

    private void StubBulkUpsertOk() =>
        factory.WireMock
            .Given(Request.Create().WithPath("/item/bulk_upsert").UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"items":[]}"""));

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
            ShopifyProductVariantId = variant.ShopifyProductVariantId,
            SkulabsSourceItemId = skulabsSourceItemId,
            SkulabsSourceListingId = $"listing-{skulabsSourceItemId}",
            Title = skulabsTitle,
            Sku = $"sku-{variantId}",
            Barcode = $"bar-{variantId}"
        });

        await db.SaveChangesAsync();
        return variant.ShopifyProductVariantId;
    }
}
