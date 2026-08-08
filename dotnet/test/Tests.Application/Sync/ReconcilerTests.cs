using Application.Skus;
using Application.Sync;
using Application.Sync.Merge;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Tests.Application.Sync;

/// <summary>
/// The reconciler decides what each variant should hold and records who is owed a push.
/// <para>
/// Assertions read the <em>desired state</em>, not the mirrors. A mirror only changes when its own
/// system says so — via ingest, or via a dispatcher confirming a push — so a reconcile that altered
/// one would be claiming Shopify or SkuLabs had said something it never did.
/// </para>
/// </summary>
public class ReconcilerTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISkuGenerator _skuGenerator = Substitute.For<ISkuGenerator>();

    public ReconcilerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _skuGenerator.Generate(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<ISet<string>?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult("GENERATED"));
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ReconcileAll_ShouldReturnEmpty_WhenNothingExists()
    {
        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
    }

    [Fact]
    public async Task ReconcileAll_ShouldCreateDesiredState_ForAVariantThatHasNone()
    {
        var variant = SeedVariant(sku: "SKU-A", barcode: "BAR-A", displayName: "Widget");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileAll();

        var desired = await _dbContext.DesiredItemStates.SingleAsync();
        desired.ShopifyProductVariantId.ShouldBe(variant.ShopifyProductVariantId);
        desired.Sku.ShouldBe("SKU-A");
        desired.Title.ShouldBe("Widget");
    }

    [Fact]
    public async Task ReconcileAll_ShouldLeaveNothingPending_WhenEverythingAlreadyAgrees()
    {
        var variant = SeedVariant(displayName: "Same", sku: "matching-sku", barcode: "matching-bar");
        SeedDesiredState(variant, sku: "matching-sku", barcode: "matching-bar", title: "Same");
        SeedSkulabsItem(variant.ShopifyProductVariantId,
            title: "Same", sku: "matching-sku", barcode: "matching-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
        (await _dbContext.ShopifyProductVariants.SingleAsync()).PendingShopifySync.ShouldBeFalse();
        (await _dbContext.SkulabsItems.SingleAsync()).PendingSkulabsSync.ShouldBeFalse();
        (await _dbContext.ShopifyProductVariantLogEvents.CountAsync()).ShouldBe(0);
    }

    // ---------- SkuLabs codes outrank ours: they may already be on a printed label ----------

    [Fact]
    public async Task ReconcileAll_ShouldAdoptSkulabsSku_AndOweShopifyAPush()
    {
        var variant = SeedVariant(sku: "shopify-has-this", barcode: "matching-bar");
        SeedDesiredState(variant, sku: "shopify-has-this", barcode: "matching-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId,
            sku: "skulabs-authoritative", barcode: "matching-bar");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.VariantsMarked.ShouldBe(1);
        (await _dbContext.DesiredItemStates.SingleAsync()).Sku.ShouldBe("skulabs-authoritative");
        (await _dbContext.ShopifyProductVariants.SingleAsync()).PendingShopifySync.ShouldBeTrue();
    }

    /// <summary>The mirror is Shopify's to change, and Shopify has not been told yet.</summary>
    [Fact]
    public async Task ReconcileAll_ShouldNotTouchTheShopifyMirror_WhenAdoptingASkulabsSku()
    {
        var variant = SeedVariant(sku: "shopify-has-this");
        SeedDesiredState(variant, sku: "shopify-has-this");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "skulabs-authoritative");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileAll();

        (await _dbContext.ShopifyProductVariants.SingleAsync()).Sku.ShouldBe("shopify-has-this");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ReconcileAll_ShouldIgnoreABlankSkulabsSku_RatherThanErasingOurs(string? skulabsSku)
    {
        var variant = SeedVariant(sku: "keep-me");
        SeedDesiredState(variant, sku: "keep-me");
        SeedSkulabsItem(variant.ShopifyProductVariantId, sku: skulabsSku ?? "");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.VariantsMarked.ShouldBe(0);
        (await _dbContext.DesiredItemStates.SingleAsync()).Sku.ShouldBe("keep-me");
    }

    [Fact]
    public async Task ReconcileAll_ShouldAdoptSkulabsBarcode_WhenItHasOne()
    {
        var variant = SeedVariant(barcode: "shopify-bar");
        SeedDesiredState(variant, barcode: "shopify-bar");
        SeedSkulabsItem(variant.ShopifyProductVariantId, barcode: "skulabs-bar");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileAll();

        (await _dbContext.DesiredItemStates.SingleAsync()).Barcode.ShouldBe("skulabs-bar");
    }

    // ---------- Titles and locations go the other way ----------

    [Fact]
    public async Task ReconcileAll_ShouldTakeTitleFromShopify_AndOweSkulabsAPush()
    {
        var variant = SeedVariant(displayName: "Shopify Name");
        SeedDesiredState(variant, title: "Shopify Name");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Stale SkuLabs Name");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.ItemsMarked.ShouldBe(1);
        (await _dbContext.SkulabsItems.SingleAsync()).PendingSkulabsSync.ShouldBeTrue();
        (await _dbContext.DesiredItemStates.SingleAsync()).Title.ShouldBe("Shopify Name");
    }

    [Fact]
    public async Task ReconcileAll_ShouldAdoptSkulabsLocation()
    {
        var variant = SeedVariant();
        SeedDesiredState(variant);
        SeedSkulabsItem(variant.ShopifyProductVariantId, location: "A-01-06");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        (await _dbContext.DesiredItemStates.SingleAsync()).Location.ShouldBe("A-01-06");
        result.ItemsMarked.ShouldBe(0, "the item already holds that location, so nothing is owed");
    }

    [Fact]
    public async Task ReconcileAll_ShouldMarkBothSides_WhenCodesAndTitleBothDisagree()
    {
        var variant = SeedVariant(displayName: "Shopify Name", sku: "shopify-sku");
        SeedDesiredState(variant, sku: "shopify-sku", title: "Shopify Name");
        SeedSkulabsItem(variant.ShopifyProductVariantId, title: "Old Name", sku: "skulabs-sku");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.VariantsMarked.ShouldBe(1);
        result.ItemsMarked.ShouldBe(1);
    }

    // ---------- Origin decides whether a payload code is trusted ----------

    /// <summary>
    /// The common way a variant is first seen on a webhook is a merchant duplicating a product
    /// without clearing its codes, so the payload SKU is presumed to be someone else's.
    /// </summary>
    [Fact]
    public async Task ReconcileVariants_ShouldReplaceThePayloadSku_OnAFirstSighting()
    {
        var variant = SeedVariant(sku: "COPIED-FROM-ORIGINAL");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileVariants(
            [variant.ShopifyProductVariantId], MergeOrigin.WebhookCreate);

        (await _dbContext.DesiredItemStates.SingleAsync()).Sku.ShouldBe("GENERATED");
    }

    /// <summary>
    /// The import cannot make the same call: a SKU regenerated now would not match the one
    /// generated when the variant was created, because the SKU derives from a since-renameable
    /// product title.
    /// </summary>
    [Fact]
    public async Task ReconcileVariants_ShouldHonourThePayloadSku_OnImport()
    {
        var variant = SeedVariant(sku: "MERCHANT-SUPPLIED");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileVariants(
            [variant.ShopifyProductVariantId], MergeOrigin.Import);

        (await _dbContext.DesiredItemStates.SingleAsync()).Sku.ShouldBe("MERCHANT-SUPPLIED");
    }

    [Fact]
    public async Task ReconcileVariants_ShouldGenerate_OnImport_WhenShopifySentNoSku()
    {
        var variant = SeedVariant(sku: "");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileVariants(
            [variant.ShopifyProductVariantId], MergeOrigin.Import);

        (await _dbContext.DesiredItemStates.SingleAsync()).Sku.ShouldBe("GENERATED");
    }

    [Fact]
    public async Task ReconcileVariants_ShouldFallBackToTheVariantId_ForABarcodeOnAFirstSighting()
    {
        var variant = SeedVariant(variantId: 4242, barcode: "");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileVariants(
            [variant.ShopifyProductVariantId], MergeOrigin.WebhookCreate);

        (await _dbContext.DesiredItemStates.SingleAsync()).Barcode.ShouldBe("4242");
    }

    /// <summary>
    /// Shopify drifting away from a decided SKU is the divergence the dispatcher exists to correct,
    /// so adopting Shopify's value here would settle the disagreement by surrendering.
    /// </summary>
    [Fact]
    public async Task ReconcileAll_ShouldKeepADecidedSku_WhenShopifyDrifts()
    {
        var variant = SeedVariant(sku: "someone-edited-this-in-shopify");
        SeedDesiredState(variant, sku: "ours");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        (await _dbContext.DesiredItemStates.SingleAsync()).Sku.ShouldBe("ours");
        result.VariantsMarked.ShouldBe(1);
        (await _dbContext.ShopifyProductVariants.SingleAsync()).PendingShopifySync.ShouldBeTrue();
    }

    // ---------- Scope ----------

    [Fact]
    public async Task ReconcileAll_ShouldSkipInactiveAndDeletedVariants()
    {
        SeedVariant(productId: 1, variantId: 1, sku: "a", isActive: false);
        SeedVariant(productId: 2, variantId: 2, sku: "b", isDeleted: true);
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileAll();

        result.ShouldBe(ReconcileResult.Empty);
        (await _dbContext.DesiredItemStates.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ReconcileVariants_ShouldOnlyTouchTheGivenVariants()
    {
        var target = SeedVariant(productId: 1, variantId: 1, sku: "");
        SeedVariant(productId: 2, variantId: 2, sku: "");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileVariants([target.ShopifyProductVariantId], MergeOrigin.Import);

        var desired = await _dbContext.DesiredItemStates.SingleAsync();
        desired.ShopifyProductVariantId.ShouldBe(target.ShopifyProductVariantId);
    }

    [Fact]
    public async Task ReconcileVariants_ShouldReturnEmpty_ForAnEmptyScope()
    {
        var result = await CreateSut().ReconcileVariants([]);

        result.ShouldBe(ReconcileResult.Empty);
    }

    [Fact]
    public async Task ReconcileSkulabsItems_ShouldReachTheLinkedVariant()
    {
        var variant = SeedVariant(sku: "shopify-sku");
        SeedDesiredState(variant, sku: "shopify-sku");
        var item = SeedSkulabsItem(variant.ShopifyProductVariantId, sku: "skulabs-sku");
        await _dbContext.SaveChangesAsync();

        var result = await CreateSut().ReconcileSkulabsItems([item.SkulabsItemId]);

        result.VariantsMarked.ShouldBe(1);
        (await _dbContext.DesiredItemStates.SingleAsync()).Sku.ShouldBe("skulabs-sku");
    }

    // ---------- Audit trail ----------

    [Fact]
    public async Task ReconcileAll_ShouldWriteOneAuditEvent_PerFieldThatMoved()
    {
        var variant = SeedVariant(sku: "old-sku", barcode: "old-bar", displayName: "Name");
        SeedDesiredState(variant, sku: "old-sku", barcode: "old-bar", title: "Name");
        SeedSkulabsItem(variant.ShopifyProductVariantId,
            title: "Name", sku: "new-sku", barcode: "new-bar");
        await _dbContext.SaveChangesAsync();

        await CreateSut().ReconcileAll();

        var messages = (await LogsForVariant(variant.ShopifyProductVariantId))
            .Select(log => log.Message)
            .ToArray();
        messages.ShouldContain("SKU changed from 'old-sku' to 'new-sku'.");
        messages.ShouldContain("Barcode changed from 'old-bar' to 'new-bar'.");
        messages.Length.ShouldBe(2, "the title already agreed, so it should not be logged");
    }

    // ---------- Helpers ----------

    private Reconciler CreateSut() => MergeTestFactory.CreateReconciler(_dbContext, _skuGenerator);

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

    private DesiredItemStateEntity SeedDesiredState(
        ShopifyProductVariantEntity variant,
        string sku = "SKU",
        string barcode = "BAR",
        string title = "Title",
        string location = "")
    {
        var entity = new DesiredItemStateEntity
        {
            DesiredItemStateId = Guid.NewGuid(),
            ShopifyProductVariantId = variant.ShopifyProductVariantId,
            Sku = sku,
            Barcode = barcode,
            Title = title,
            Location = location
        };
        _dbContext.DesiredItemStates.Add(entity);
        return entity;
    }

    private SkulabsItemEntity SeedSkulabsItem(
        Guid variantGuid,
        string sourceItemId = "src",
        string sourceListingId = "lst",
        string title = "Title",
        string sku = "SKU",
        string barcode = "BAR",
        string location = "")
    {
        var entity = new SkulabsItemEntity
        {
            SkulabsItemId = Guid.NewGuid(),
            SkulabsSourceItemId = sourceItemId,
            Title = title,
            Sku = sku,
            Barcode = barcode,
            Location = location,
            Listings =
            {
                new SkulabsItemListingEntity
                {
                    SkulabsItemListingId = Guid.NewGuid(),
                    SkulabsSourceListingId = sourceListingId,
                    RawVariantId = variantGuid.ToString(),
                    ShopifyProductId = "prod",
                    ShopifyProductVariantId = variantGuid
                }
            }
        };
        _dbContext.SkulabsItems.Add(entity);
        return entity;
    }
}
