using Application.Skulabs.Jobs;
using Application.Skulabs.Services;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
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
public class SkulabsItemSyncTests(E2EWebApplicationFactory factory) : IAsyncLifetime
{
    private const long MatchingVariantId = 45696210862241L;

    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SkulabsSyncJob_CreatesSkulabsItem_WhenMatchingVariantExistsAndNoneInDatabase()
    {
        var variantGuid = await SeedVariantAsync(MatchingVariantId);
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-single.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.SkulabsItems.SingleAsync();
        stored.ShopifyProductVariantId.ShouldBe(variantGuid);
        stored.SkulabsSourceItemId.ShouldBe("69b4543c6642ed434a5b1c4a");
        stored.SkulabsSourceListingId.ShouldBe("69b454b06642ed434a5bf571");
        stored.Sku.ShouldBe("1 bird");
        stored.Barcode.ShouldBe("10862241");
        stored.Title.ShouldBe("Yellow Vintage Nature Domino Necklace (Goose (1bird))");

        var logs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == variantGuid)
            .ToListAsync();
        logs.Count.ShouldBe(1);
        logs[0].Message.ShouldBe("Linked to SkuLabs item '69b4543c6642ed434a5b1c4a'.");
    }

    [Fact]
    public async Task SkulabsSyncJob_IsNoOp_WhenSameLinkAlreadyExists_EvenIfMetadataDiffers()
    {
        // Same SkuLabs source item id, same variant — metadata diverges between DB and API.
        // Contract: link writes are decided by IDs alone, so this is a no-op.
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
        var stored = await db.SkulabsItems.SingleAsync();
        // Same row, untouched — PK preserved, metadata still the original seed values.
        stored.SkulabsItemId.ShouldBe(existingItemId);
        stored.ShopifyProductVariantId.ShouldBe(variantGuid);
        stored.Title.ShouldBe("Old Title");
        stored.Sku.ShouldBe("old-sku");
        stored.Barcode.ShouldBe("old-barcode");

        (await db.ShopifyProductVariantLogEvents
            .CountAsync(l => l.ShopifyProductVariantId == variantGuid)).ShouldBe(0);
    }

    [Fact]
    public async Task SkulabsSyncJob_RelinksToNewVariant_WhenSkulabsItemMovesVariants()
    {
        // DB:  oldVariant ↔ skulabs item S.
        // API: skulabs item S now points at newVariant (99999999999999).
        // Expected: link severed from oldVariant, established on newVariant; metadata refreshed.
        var oldVariantGuid = await SeedVariantAsync(MatchingVariantId);
        var newVariantGuid = await SeedVariantAsync(99999999999999L);
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
        var stored = await db.SkulabsItems.SingleAsync();
        // Same PK — the row was re-pointed, not deleted + recreated.
        stored.SkulabsItemId.ShouldBe(rowId);
        stored.ShopifyProductVariantId.ShouldBe(newVariantGuid);
        // Metadata refreshed from the API payload because a new link was written.
        stored.Title.ShouldBe("Yellow Vintage Nature Domino Necklace (Goose (1bird))");
        stored.Sku.ShouldBe("1 bird");
        stored.Barcode.ShouldBe("10862241");

        var oldLogs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == oldVariantGuid)
            .ToListAsync();
        oldLogs.Single().Message.ShouldBe("Unlinked from SkuLabs item '69b4543c6642ed434a5b1c4a'.");

        var newLogs = await db.ShopifyProductVariantLogEvents
            .Where(l => l.ShopifyProductVariantId == newVariantGuid)
            .ToListAsync();
        newLogs.Single().Message.ShouldBe("Linked to SkuLabs item '69b4543c6642ed434a5b1c4a'.");
    }

    [Fact]
    public async Task SkulabsSyncJob_DoesNothing_WhenNoMatchingVariantExists()
    {
        // No variant seeded with VariantId 45696210862241.
        await StubSkulabsGetAllAsync("Skulabs/Api/items-get-single.json");

        await RunSyncJobAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.SkulabsItems.CountAsync()).ShouldBe(0);
    }

    private async Task RunSyncJobAsync()
    {
        // Quartz's AddJob<T> doesn't register the job class itself in the DI container,
        // so we resolve its dependencies and construct it directly. This still exercises
        // the real sync service, real SkuLabs HTTP client (hitting WireMock), real DbContext
        // and real in-memory message bus end-to-end.
        using var scope = factory.Services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<ISkulabsItemSyncService>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var featureManager = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SkulabsItemSyncJob>>();
        var job = new SkulabsItemSyncJob(syncService, messageBus, featureManager, logger);

        var context = Substitute.For<IJobExecutionContext>();
        var trigger = Substitute.For<ITrigger>();
        trigger.Key.Returns(new TriggerKey("e2e-trigger"));
        context.Trigger.Returns(trigger);
        context.CancellationToken.Returns(CancellationToken.None);
        context.FireTimeUtc.Returns(DateTimeOffset.UtcNow);

        await job.Execute(context);
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

    private async Task<Guid> SeedVariantAsync(long variantId)
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
            DisplayName = "Test Variant",
            Sku = "seed-sku",
            Barcode = "seed-barcode"
        };
        db.ShopifyProductVariants.Add(entity);
        await db.SaveChangesAsync();
        return entity.ShopifyProductVariantId;
    }

    private async Task<Guid> SeedSkulabsItemAsync(
        Guid variantGuid,
        string sourceItemId,
        string sourceListingId,
        string title,
        string sku,
        string barcode)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entity = new SkulabsItemEntity
        {
            SkulabsItemId = Guid.CreateVersion7(),
            ShopifyProductVariantId = variantGuid,
            SkulabsSourceItemId = sourceItemId,
            SkulabsSourceListingId = sourceListingId,
            Title = title,
            Sku = sku,
            Barcode = barcode
        };
        db.SkulabsItems.Add(entity);
        await db.SaveChangesAsync();
        return entity.SkulabsItemId;
    }
}
