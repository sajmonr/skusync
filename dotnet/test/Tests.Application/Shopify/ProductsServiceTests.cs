using Application.Products.Services;
using Application.Skus;
using Application.Sync;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Shopify;

public class ProductsServiceTests : IDisposable
{
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly IShopifyDispatchTrigger _dispatchTrigger = Substitute.For<IShopifyDispatchTrigger>();
    private readonly ISkuGenerator _skuGenerator = Substitute.For<ISkuGenerator>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ProductsService> _logger = new();

    public ProductsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ImportProducts_ShouldCreateVariant_WhenVariantNotInDatabase()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "Blue T-Shirt - Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Count.ShouldBe(1);
        variants[0].GlobalProductId.ShouldBe("gid://shopify/Product/100");
        variants[0].GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/200");
        variants[0].ProductId.ShouldBe(100L);
        variants[0].VariantId.ShouldBe(200L);
        variants[0].DisplayName.ShouldBe("Blue T-Shirt - Large");
        variants[0].Sku.ShouldBe("SKU-1");
        variants[0].Barcode.ShouldBe("BAR-1");
    }

    [Fact]
    public async Task ImportProducts_ShouldUpdateDisplayName_WhenDisplayNameDiffersFromDatabase()
    {
        var existingVariant = SeedVariant("gid://shopify/ProductVariant/200", displayName: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "New Title",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.DisplayName.ShouldBe("New Title");
        updated.UpdatedOnUtc.ShouldBeGreaterThanOrEqualTo(existingVariant.UpdatedOnUtc);

        var logEvents = await _dbContext.Set<ShopifyProductVariantLogEventEntity>()
            .Where(e => e.ShopifyProductVariantId == existingVariant.ShopifyProductVariantId)
            .ToListAsync();
        logEvents.ShouldContain(e => e.Message.Contains("Old Title") && e.Message.Contains("New Title"));
    }

    [Fact]
    public async Task ImportProducts_ShouldMatchAndUpdateInactiveVariant_WithoutReInserting()
    {
        // A deactivated row must be matched on its GlobalVariantId — otherwise the import treats
        // it as new and the insert violates the unique index, failing the whole batch. The
        // import does not reactivate it (see issue #32); that remains the drift sweep's job.
        var inactive = SeedVariant(
            "gid://shopify/ProductVariant/200",
            displayName: "Old Title",
            sku: "SKU-1",
            barcode: "BAR-1",
            isActive: false);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "New Title",
                "SKU-1",
                "BAR-1")
        ]);

        var result = await CreateSut().ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();

        var rows = await _dbContext.Set<ShopifyProductVariantEntity>()
            .Where(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200")
            .ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].ShopifyProductVariantId.ShouldBe(inactive.ShopifyProductVariantId);
        rows[0].DisplayName.ShouldBe("New Title");
        rows[0].IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task ImportProducts_ShouldNotUpdateSku_WhenSkuAlreadySetInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt", sku: "OLD-SKU", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "NEW-SKU",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Sku.ShouldBe("OLD-SKU");
    }

    [Fact]
    public async Task ImportProducts_ShouldUpdateSku_WhenSkuIsEmptyInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt", sku: "", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "NEW-SKU",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Sku.ShouldBe("NEW-SKU");
    }

    [Fact]
    public async Task ImportProducts_ShouldGenerateSku_WhenShopifyVariantHasNoSku_OnCreate()
    {
        _skuGenerator.Generate(
                "T-Shirt", "Large",
                Arg.Any<ISet<string>?>(), "200", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("BW-TSh-LG"));

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt - Large",
                Sku: "",
                Barcode: "BAR-1")
            {
                ProductTitle = "T-Shirt",
                VariantTitle = "Large",
            }
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var saved = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        saved.Sku.ShouldBe("BW-TSh-LG");

        // SkuSet log event recorded (separate from the VariantCreated event).
        var logMessages = await _dbContext.ShopifyProductVariantLogEvents
            .Where(e => e.ShopifyProductVariantId == saved.ShopifyProductVariantId)
            .Select(e => e.Message)
            .ToListAsync();
        logMessages.ShouldContain(msg => msg.Contains("BW-TSh-LG"));
    }

    [Fact]
    public async Task ImportProducts_ShouldGenerateSku_WhenBothExistingAndShopifySkusAreEmpty_OnUpdate()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt - Large", sku: "", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _skuGenerator.Generate(
                "T-Shirt", "Large",
                Arg.Any<ISet<string>?>(), "200", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("BW-TSh-LG"));

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt - Large",
                Sku: "",
                Barcode: "BAR-1")
            {
                ProductTitle = "T-Shirt",
                VariantTitle = "Large",
            }
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Sku.ShouldBe("BW-TSh-LG");
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnFailure_WhenSkuGeneratorThrows()
    {
        _skuGenerator.Generate(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<ISet<string>?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("generator unhappy"));

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt - Large",
                Sku: "",
                Barcode: "BAR-1")
            {
                ProductTitle = "T-Shirt",
                VariantTitle = "Large",
            }
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProductsFromShopify();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();

        // Nothing should have been persisted.
        var saved = await _dbContext.Set<ShopifyProductVariantEntity>().CountAsync();
        saved.ShouldBe(0);

        // The exception is logged at Error level.
        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Error);
    }

    [Fact]
    public async Task ImportProducts_ShouldImportBadAndGoodProducts_WhenOneTitleIsUnabbreviatable()
    {
        // Regression for #38: an emoji-only title strips to an empty abbreviation. The SKU
        // generator used to throw, and the catch in ImportProductsFromShopify failed the whole
        // batch — one bad product aborted every other product in the import. It must now fall
        // back to a variant-id-derived SKU so the bad product imports alongside the good ones.
        // Uses the real generator so the throw path is exercised.
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "🎁 - Small / Black",
                Sku: "",
                Barcode: "BAR-1")
            {
                ProductTitle = "🎁",
                VariantTitle = "Small / Black",
            },
            new ShopifyProductVariant(
                "gid://shopify/Product/101",
                "gid://shopify/ProductVariant/201",
                "Basic Tee - Large",
                Sku: "SKU-OK",
                Barcode: "BAR-2")
            {
                ProductTitle = "Basic Tee",
                VariantTitle = "Large",
            }
        ]);

        var result = await CreateSutWithRealGenerator().ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(2);

        var unabbreviatable = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        unabbreviatable.Sku.ShouldBe("BW-200-SM-BL");

        var good = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/201");
        good.Sku.ShouldBe("SKU-OK");
    }

    [Fact]
    public async Task ImportProducts_ShouldNotUpdateBarcode_WhenBarcodeAlreadySetInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt", sku: "SKU-1", barcode: "OLD-BAR");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "SKU-1",
                "NEW-BAR")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Barcode.ShouldBe("OLD-BAR");
    }

    [Fact]
    public async Task ImportProducts_ShouldUpdateBarcode_WhenBarcodeIsEmptyInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt", sku: "SKU-1", barcode: "");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "SKU-1",
                "NEW-BAR")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Barcode.ShouldBe("NEW-BAR");
    }

    [Fact]
    public async Task ImportProducts_ShouldNotUpdateVariant_WhenAllFieldsMatch()
    {
        var existingVariant = SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt - Large", sku: "SKU-1", barcode: "BAR-1");
        var originalUpdatedOn = existingVariant.UpdatedOnUtc;
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt - Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var variant = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        variant.UpdatedOnUtc.ShouldBe(originalUpdatedOn);
    }

    [Fact]
    public async Task ImportProducts_ShouldSetUpdatedOnUtc_WhenVariantIsUpdated()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "New Title",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.UpdatedOnUtc.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task ImportProducts_ShouldHandleMixedCreateAndUpdate()
    {
        SeedVariant("gid://shopify/ProductVariant/100", displayName: "Existing", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/10",
                "gid://shopify/ProductVariant/100",
                "Updated Title",
                "SKU-A",
                "BAR-A"),
            new ShopifyProductVariant(
                "gid://shopify/Product/20",
                "gid://shopify/ProductVariant/200",
                "New Variant",
                "SKU-B",
                "BAR-B")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Count.ShouldBe(2);

        var existingVariant = variants.Single(v => v.GlobalVariantId == "gid://shopify/ProductVariant/100");
        existingVariant.DisplayName.ShouldBe("Updated Title");

        var newVariant = variants.Single(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        newVariant.DisplayName.ShouldBe("New Variant");
        newVariant.Sku.ShouldBe("SKU-B");
        newVariant.Barcode.ShouldBe("BAR-B");
    }

    [Fact]
    public async Task ImportProducts_ShouldCreateSingleVariant_WhenShopifyReturnsSameGlobalVariantIdTwiceInBatch()
    {
        // Shopify can return the same variant more than once in one payload. Each repeat must
        // fold into the single pending insert; queueing a second insert would violate the
        // unique index on GlobalVariantId once the changes are flushed.
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt", "SKU-1", "BAR-1"),
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt", "SKU-1", "BAR-1")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(1);

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Count.ShouldBe(1);
        variants[0].GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/200");
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnFailureResult_WhenShopifyCallFails()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _shopifyProductService.GetProducts().ThrowsAsync(exception);

        var sut = CreateSut();

        var result = await sut.ImportProductsFromShopify();

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();

        var errorLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errorLogs.Length.ShouldBe(1);
        errorLogs[0].Exception.ShouldBeSameAs(exception);
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnSuccessWithCreatedCount_WhenNewVariantsImported()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/1", "gid://shopify/ProductVariant/1", "Shirt", "SKU-1", "BAR-1"),
            new ShopifyProductVariant("gid://shopify/Product/2", "gid://shopify/ProductVariant/2", "Pants", "SKU-2", "BAR-2")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(2);
        result.Updated.ShouldBe(0);
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnSuccessWithUpdatedCount_WhenExistingVariantsChanged()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "New Title", "SKU-1", "BAR-1")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(0);
        result.Updated.ShouldBe(1);
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnSuccessWithZeroCounts_WhenNoChanges()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt - Large", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt - Large", "SKU-1", "BAR-1")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(0);
        result.Updated.ShouldBe(0);
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnCorrectCounts_WhenMixedCreateAndUpdate()
    {
        SeedVariant("gid://shopify/ProductVariant/100", displayName: "Old Title", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/10", "gid://shopify/ProductVariant/100", "New Title", "SKU-A", "BAR-A"),
            new ShopifyProductVariant("gid://shopify/Product/20", "gid://shopify/ProductVariant/200", "Brand New", "SKU-B", "BAR-B")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(1);
        result.Updated.ShouldBe(1);
    }

    [Fact]
    public async Task ImportProducts_ShouldLogDebugStatements_DuringSuccessfulSync()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProductsFromShopify();

        var debugLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Debug).ToArray();
        debugLogs.Length.ShouldBeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // Removal reconciliation — variants absent from the full Shopify import
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ImportProducts_ShouldMarkVariantDeleted_WhenAbsentFromShopifyPayload()
    {
        var present = SeedVariant("gid://shopify/ProductVariant/100", displayName: "Present", sku: "SKU-A", barcode: "BAR-A", variantId: 100);
        var removed = SeedVariant("gid://shopify/ProductVariant/200", displayName: "Removed", sku: "SKU-B", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/100", "Present", "SKU-A", "BAR-A")
        ]);

        await CreateSut().ImportProductsFromShopify();

        var removedRow = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.ShopifyProductVariantId == removed.ShopifyProductVariantId);
        removedRow.IsDeleted.ShouldBeTrue();
        removedRow.DeletedOn.ShouldBeGreaterThan(DateTime.MinValue);

        var presentRow = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.ShopifyProductVariantId == present.ShopifyProductVariantId);
        presentRow.IsDeleted.ShouldBeFalse();

        var logMessages = await _dbContext.ShopifyProductVariantLogEvents
            .Where(e => e.ShopifyProductVariantId == removed.ShopifyProductVariantId)
            .Select(e => e.Message)
            .ToListAsync();
        logMessages.ShouldContain(m => m.Contains("deleted"));
    }

    [Fact]
    public async Task ImportProducts_ShouldNotDeleteEntireCatalog_WhenShopifyReturnsEmpty()
    {
        SeedVariant("gid://shopify/ProductVariant/100", displayName: "Kept", sku: "SKU-A", barcode: "BAR-A", variantId: 100);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns([]);

        await CreateSut().ImportProductsFromShopify();

        var row = await _dbContext.Set<ShopifyProductVariantEntity>().SingleAsync();
        row.IsDeleted.ShouldBeFalse();
        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task ImportProducts_ShouldLeaveDeletedVariantFrozen_WhenItReappearsInPayload()
    {
        var deleted = SeedVariant("gid://shopify/ProductVariant/200", displayName: "Frozen Name", sku: "SKU-A", barcode: "BAR-A", variantId: 200, isDeleted: true);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "New Name", "SKU-A", "NEW-BAR")
        ]);

        var result = await CreateSut().ImportProductsFromShopify();

        result.IsSuccess.ShouldBeTrue();
        var row = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.ShopifyProductVariantId == deleted.ShopifyProductVariantId);
        row.IsDeleted.ShouldBeTrue();
        row.DisplayName.ShouldBe("Frozen Name");
        result.Updated.ShouldBe(0);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldReturnSuccessWithEmptyArray_WhenNoDuplicatesExist()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "SKU-A", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "SKU-B", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.DeduplicateProducts();

        result.IsSuccess.ShouldBeTrue();
        result.VariantIds.ShouldBeEmpty();
        result.Error.ShouldBe("");
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldReturnAffectedIds_WhenDuplicateSkuFound()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.DeduplicateProducts();

        result.IsSuccess.ShouldBeTrue();
        result.VariantIds.Length.ShouldBe(2);
        result.VariantIds.ShouldContain(100L);
        result.VariantIds.ShouldContain(200L);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldReturnAffectedIds_WhenDuplicateBarcodeFound()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "SKU-A", barcode: "DUPE-BAR", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "SKU-B", barcode: "DUPE-BAR", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.DeduplicateProducts();

        result.IsSuccess.ShouldBeTrue();
        result.VariantIds.Length.ShouldBe(2);
        result.VariantIds.ShouldContain(100L);
        result.VariantIds.ShouldContain(200L);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldReturnAllAffectedIds_WhenBothSkuAndBarcodeHaveSeparateDuplicates()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "BAR-B", variantId: 200);
        SeedVariant("gid://shopify/ProductVariant/300", sku: "SKU-C", barcode: "DUPE-BAR", variantId: 300);
        SeedVariant("gid://shopify/ProductVariant/400", sku: "SKU-D", barcode: "DUPE-BAR", variantId: 400);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.DeduplicateProducts();

        result.IsSuccess.ShouldBeTrue();
        result.VariantIds.Length.ShouldBe(4);
        result.VariantIds.ShouldContain(100L);
        result.VariantIds.ShouldContain(200L);
        result.VariantIds.ShouldContain(300L);
        result.VariantIds.ShouldContain(400L);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldSetSkuToVariantId_WhenSkuIsDuplicated()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        await sut.DeduplicateProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Single(v => v.VariantId == 100).Sku.ShouldBe("100");
        variants.Single(v => v.VariantId == 200).Sku.ShouldBe("200");
        // Barcode was not duplicated — it should remain unchanged
        variants.Single(v => v.VariantId == 100).Barcode.ShouldBe("BAR-A");
        variants.Single(v => v.VariantId == 200).Barcode.ShouldBe("BAR-B");
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldSetBarcodeToVariantId_WhenBarcodeIsDuplicated()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "SKU-A", barcode: "DUPE-BAR", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "SKU-B", barcode: "DUPE-BAR", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        await sut.DeduplicateProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Single(v => v.VariantId == 100).Barcode.ShouldBe("100");
        variants.Single(v => v.VariantId == 200).Barcode.ShouldBe("200");
        // SKU was not duplicated — it should remain unchanged
        variants.Single(v => v.VariantId == 100).Sku.ShouldBe("SKU-A");
        variants.Single(v => v.VariantId == 200).Sku.ShouldBe("SKU-B");
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldSetBothSkuAndBarcodeToVariantId_WhenBothAreDuplicated()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "DUPE-BAR", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "DUPE-BAR", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        await sut.DeduplicateProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Single(v => v.VariantId == 100).Sku.ShouldBe("100");
        variants.Single(v => v.VariantId == 200).Sku.ShouldBe("200");
        variants.Single(v => v.VariantId == 100).Barcode.ShouldBe("100");
        variants.Single(v => v.VariantId == 200).Barcode.ShouldBe("200");
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldNotModifyUniqueVariants_WhenOnlySomeVariantsAreDuplicated()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "BAR-B", variantId: 200);
        SeedVariant("gid://shopify/ProductVariant/300", sku: "UNIQUE-SKU", barcode: "UNIQUE-BAR", variantId: 300);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.DeduplicateProducts();

        result.VariantIds.ShouldNotContain(300L);
        var uniqueVariant = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.VariantId == 300);
        uniqueVariant.Sku.ShouldBe("UNIQUE-SKU");
        uniqueVariant.Barcode.ShouldBe("UNIQUE-BAR");
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldNotIgnoreEmptySkus_WhenCheckingForDuplicates()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.DeduplicateProducts();

        result.IsSuccess.ShouldBeTrue();
        result.VariantIds.Length.ShouldBe(2);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldNotIgnoreEmptyBarcodes_WhenCheckingForDuplicates()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "SKU-A", barcode: "", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "SKU-B", barcode: "", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.DeduplicateProducts();

        result.IsSuccess.ShouldBeTrue();
        result.VariantIds.Length.ShouldBe(2);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldSetUpdatedOnUtc_WhenVariantIsDeduplicated()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        await sut.DeduplicateProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Single(v => v.VariantId == 100).UpdatedOnUtc.ShouldBeGreaterThan(before);
        variants.Single(v => v.VariantId == 200).UpdatedOnUtc.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldLogInformation_WhenDeduplicationCompletes()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        await sut.DeduplicateProducts();

        var infoLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Information).ToArray();
        infoLogs.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldMarkRewrittenVariantsPending_AndDispatchThem()
    {
        var first = SeedVariant("gid://shopify/ProductVariant/100", sku: "DUPE-SKU", barcode: "BAR-A", variantId: 100);
        var second = SeedVariant("gid://shopify/ProductVariant/200", sku: "DUPE-SKU", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        await sut.DeduplicateProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.ShouldAllBe(v => v.PendingShopifySync);
        await _dispatchTrigger.Received(1).TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 2
                && ids.Contains(first.ShopifyProductVariantId)
                && ids.Contains(second.ShopifyProductVariantId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeduplicateProducts_ShouldNotDispatch_WhenNoDuplicatesExist()
    {
        SeedVariant("gid://shopify/ProductVariant/100", sku: "SKU-A", barcode: "BAR-A", variantId: 100);
        SeedVariant("gid://shopify/ProductVariant/200", sku: "SKU-B", barcode: "BAR-B", variantId: 200);
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();

        await sut.DeduplicateProducts();

        await _dispatchTrigger.DidNotReceive().TryDispatch(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Dispatch triggering and pending marking — ImportProducts
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ImportProducts_ShouldDispatchCreatedVariant_WithoutMarkingPending_WhenSkuComesFromShopify()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt", "SKU-1", "BAR-1")
        ]);

        await CreateSut().ImportProductsFromShopify();

        var created = await _dbContext.Set<ShopifyProductVariantEntity>().SingleAsync();
        created.PendingShopifySync.ShouldBeFalse();
        await _dispatchTrigger.Received(1).TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(created.ShopifyProductVariantId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportProducts_ShouldMarkCreatedVariantPending_WhenSkuWasGenerated()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt - Large",
                Sku: "",
                Barcode: "BAR-1")
            {
                ProductTitle = "T-Shirt",
                VariantTitle = "Large",
            }
        ]);

        await CreateSut().ImportProductsFromShopify();

        var created = await _dbContext.Set<ShopifyProductVariantEntity>().SingleAsync();
        created.PendingShopifySync.ShouldBeTrue();
    }

    [Fact]
    public async Task ImportProducts_ShouldDispatchUpdatedVariant_WhenExistingVariantIsChanged()
    {
        var seeded = SeedVariant("gid://shopify/ProductVariant/200", displayName: "Old Title", sku: "SKU-1", barcode: "BAR-1", variantId: 200);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "New Title", "SKU-1", "BAR-1")
        ]);

        await CreateSut().ImportProductsFromShopify();

        await _dispatchTrigger.Received(1).TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(seeded.ShopifyProductVariantId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportProducts_ShouldMarkUpdatedVariantPending_WhenLocalSkuDiffersFromShopify()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt", sku: "OLD-SKU", barcode: "BAR-1", variantId: 200);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt", "NEW-SKU", "BAR-1")
        ]);

        await CreateSut().ImportProductsFromShopify();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>().SingleAsync();
        updated.PendingShopifySync.ShouldBeTrue();
    }

    [Fact]
    public async Task ImportProducts_ShouldNotDispatchAnyVariant_WhenNoChangesOccur()
    {
        SeedVariant("gid://shopify/ProductVariant/200", displayName: "T-Shirt - Large", sku: "SKU-1", barcode: "BAR-1", variantId: 200);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt - Large", "SKU-1", "BAR-1")
        ]);

        await CreateSut().ImportProductsFromShopify();

        // The import always calls TryDispatch after its save — with an empty id set when
        // nothing changed — so assert no call carried any variant id.
        var variant = await _dbContext.Set<ShopifyProductVariantEntity>().SingleAsync();
        variant.PendingShopifySync.ShouldBeFalse();
        await _dispatchTrigger.DidNotReceive().TryDispatch(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count > 0),
            Arg.Any<CancellationToken>());
    }

    private ShopifyProductVariantEntity SeedVariant(
        string globalVariantId,
        string globalProductId = "gid://shopify/Product/100",
        string displayName = "Variant",
        string sku = "SKU",
        string barcode = "BAR",
        long variantId = 200,
        long productId = 100,
        bool isActive = true,
        bool isDeleted = false)
    {
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = globalProductId,
            ProductId = productId,
            GlobalVariantId = globalVariantId,
            VariantId = variantId,
            DisplayName = displayName,
            Sku = sku,
            Barcode = barcode,
            IsActive = isActive,
            IsDeleted = isDeleted
        };

        _dbContext.Set<ShopifyProductVariantEntity>().Add(entity);
        return entity;
    }

    private ProductsService CreateSut() => new(_shopifyProductService, _dbContext, _logger, _dispatchTrigger, _skuGenerator);

    private ProductsService CreateSutWithRealGenerator()
    {
        var skuGenerator = new SkuGenerator(
            _dbContext, Options.Create(new SkuGeneratorOptions()), NullLogger<SkuGenerator>.Instance);
        return new(_shopifyProductService, _dbContext, _logger, _dispatchTrigger, skuGenerator);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
