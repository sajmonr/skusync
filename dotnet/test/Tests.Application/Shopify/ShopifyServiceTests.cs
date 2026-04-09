using Application.Events;
using Application.Shopify;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Shopify;

public class ShopifyServiceTests : IDisposable
{
    private readonly IShopifyProductService _shopifyProductService = Substitute.For<IShopifyProductService>();
    private readonly IProductEventAccumulator _eventAccumulator = Substitute.For<IProductEventAccumulator>();
    private readonly ApplicationDbContext _dbContext;
    private readonly TestLogger<ShopifyService> _logger = new();

    public ShopifyServiceTests()
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
                "Blue T-Shirt",
                "Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Count.ShouldBe(1);
        variants[0].GlobalProductId.ShouldBe("gid://shopify/Product/100");
        variants[0].GlobalVariantId.ShouldBe("gid://shopify/ProductVariant/200");
        variants[0].ProductId.ShouldBe(100L);
        variants[0].VariantId.ShouldBe(200L);
        variants[0].ProductTitle.ShouldBe("Blue T-Shirt");
        variants[0].VariantTitle.ShouldBe("Large");
        variants[0].Sku.ShouldBe("SKU-1");
        variants[0].Barcode.ShouldBe("BAR-1");
    }

    [Fact]
    public async Task ImportProducts_ShouldUpdateTitle_WhenTitleDiffersFromDatabase()
    {
        var existingVariant = SeedVariant("gid://shopify/ProductVariant/200", title: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "New Title",
                "",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.ProductTitle.ShouldBe("New Title");
        updated.UpdatedOnUtc.ShouldBeGreaterThanOrEqualTo(existingVariant.UpdatedOnUtc);
    }

    [Fact]
    public async Task ImportProducts_ShouldUpdateVariantTitle_WhenVariantTitleDiffersFromDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", variantTitle: "Small", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.VariantTitle.ShouldBe("Large");
    }

    [Fact]
    public async Task ImportProducts_ShouldNotUpdateSku_WhenSkuAlreadySetInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "OLD-SKU", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "NEW-SKU",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Sku.ShouldBe("OLD-SKU");
    }

    [Fact]
    public async Task ImportProducts_ShouldUpdateSku_WhenSkuIsEmptyInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "NEW-SKU",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Sku.ShouldBe("NEW-SKU");
    }

    [Fact]
    public async Task ImportProducts_ShouldNotUpdateBarcode_WhenBarcodeAlreadySetInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "SKU-1", barcode: "OLD-BAR");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "SKU-1",
                "NEW-BAR")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Barcode.ShouldBe("OLD-BAR");
    }

    [Fact]
    public async Task ImportProducts_ShouldUpdateBarcode_WhenBarcodeIsEmptyInDatabase()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", sku: "SKU-1", barcode: "");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "",
                "SKU-1",
                "NEW-BAR")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.Barcode.ShouldBe("NEW-BAR");
    }

    [Fact]
    public async Task ImportProducts_ShouldNotUpdateVariant_WhenAllFieldsMatch()
    {
        var existingVariant = SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", variantTitle: "Large", sku: "SKU-1", barcode: "BAR-1");
        var originalUpdatedOn = existingVariant.UpdatedOnUtc;
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "T-Shirt",
                "Large",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var variant = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        variant.UpdatedOnUtc.ShouldBe(originalUpdatedOn);
    }

    [Fact]
    public async Task ImportProducts_ShouldSetUpdatedOnUtc_WhenVariantIsUpdated()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        SeedVariant("gid://shopify/ProductVariant/200", title: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/100",
                "gid://shopify/ProductVariant/200",
                "New Title",
                "",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var updated = await _dbContext.Set<ShopifyProductVariantEntity>()
            .SingleAsync(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        updated.UpdatedOnUtc.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task ImportProducts_ShouldHandleMixedCreateAndUpdate()
    {
        SeedVariant("gid://shopify/ProductVariant/100", title: "Existing", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant(
                "gid://shopify/Product/10",
                "gid://shopify/ProductVariant/100",
                "Updated Title",
                "",
                "SKU-A",
                "BAR-A"),
            new ShopifyProductVariant(
                "gid://shopify/Product/20",
                "gid://shopify/ProductVariant/200",
                "New Variant",
                "",
                "SKU-B",
                "BAR-B")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var variants = await _dbContext.Set<ShopifyProductVariantEntity>().ToListAsync();
        variants.Count.ShouldBe(2);

        var existingVariant = variants.Single(v => v.GlobalVariantId == "gid://shopify/ProductVariant/100");
        existingVariant.ProductTitle.ShouldBe("Updated Title");

        var newVariant = variants.Single(v => v.GlobalVariantId == "gid://shopify/ProductVariant/200");
        newVariant.ProductTitle.ShouldBe("New Variant");
        newVariant.Sku.ShouldBe("SKU-B");
        newVariant.Barcode.ShouldBe("BAR-B");
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnFailureResult_WhenShopifyCallFails()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _shopifyProductService.GetProducts().ThrowsAsync(exception);

        var sut = CreateSut();

        var result = await sut.ImportProducts();

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
            new ShopifyProductVariant("gid://shopify/Product/1", "gid://shopify/ProductVariant/1", "Shirt", "", "SKU-1", "BAR-1"),
            new ShopifyProductVariant("gid://shopify/Product/2", "gid://shopify/ProductVariant/2", "Pants", "", "SKU-2", "BAR-2")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProducts();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(2);
        result.Updated.ShouldBe(0);
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnSuccessWithUpdatedCount_WhenExistingVariantsChanged()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "Old Title", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "New Title", "", "SKU-1", "BAR-1")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProducts();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(0);
        result.Updated.ShouldBe(1);
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnSuccessWithZeroCounts_WhenNoChanges()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", variantTitle: "Large", sku: "SKU-1", barcode: "BAR-1");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt", "Large", "SKU-1", "BAR-1")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProducts();

        result.IsSuccess.ShouldBeTrue();
        result.Created.ShouldBe(0);
        result.Updated.ShouldBe(0);
    }

    [Fact]
    public async Task ImportProducts_ShouldReturnCorrectCounts_WhenMixedCreateAndUpdate()
    {
        SeedVariant("gid://shopify/ProductVariant/100", title: "Old Title", sku: "SKU-A", barcode: "BAR-A");
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/10", "gid://shopify/ProductVariant/100", "New Title", "", "SKU-A", "BAR-A"),
            new ShopifyProductVariant("gid://shopify/Product/20", "gid://shopify/ProductVariant/200", "Brand New", "", "SKU-B", "BAR-B")
        ]);

        var sut = CreateSut();

        var result = await sut.ImportProducts();

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
                "",
                "SKU-1",
                "BAR-1")
        ]);

        var sut = CreateSut();

        await sut.ImportProducts();

        var debugLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Debug).ToArray();
        debugLogs.Length.ShouldBeGreaterThan(0);
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

    // -------------------------------------------------------------------------
    // Event accumulation — ImportProducts
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ImportProducts_ShouldEnqueueCreatedEvent_WhenNewVariantIsSaved()
    {
        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt", "", "SKU-1", "BAR-1")
        ]);

        await CreateSut().ImportProducts();

        _eventAccumulator.Received(1).Enqueue(
            Arg.Is<ProductChangedEvent>(e => e.VariantId == 200L && e.ChangeType == ProductChangeType.Created));
    }

    [Fact]
    public async Task ImportProducts_ShouldEnqueueUpdatedEvent_WhenExistingVariantIsChanged()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "Old Title", sku: "SKU-1", barcode: "BAR-1", variantId: 200);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "New Title", "", "SKU-1", "BAR-1")
        ]);

        await CreateSut().ImportProducts();

        _eventAccumulator.Received(1).Enqueue(
            Arg.Is<ProductChangedEvent>(e => e.VariantId == 200L && e.ChangeType == ProductChangeType.Updated));
    }

    [Fact]
    public async Task ImportProducts_ShouldNotEnqueueAnyEvent_WhenNoChangesOccur()
    {
        SeedVariant("gid://shopify/ProductVariant/200", title: "T-Shirt", variantTitle: "Large", sku: "SKU-1", barcode: "BAR-1", variantId: 200);
        await _dbContext.SaveChangesAsync();

        _shopifyProductService.GetProducts().Returns(
        [
            new ShopifyProductVariant("gid://shopify/Product/100", "gid://shopify/ProductVariant/200", "T-Shirt", "Large", "SKU-1", "BAR-1")
        ]);

        await CreateSut().ImportProducts();

        _eventAccumulator.DidNotReceive().Enqueue(Arg.Any<ProductChangedEvent>());
    }

    private ShopifyProductVariantEntity SeedVariant(
        string globalVariantId,
        string globalProductId = "gid://shopify/Product/100",
        string title = "Variant",
        string variantTitle = "",
        string sku = "SKU",
        string barcode = "BAR",
        long variantId = 200,
        long productId = 100)
    {
        var fullTitle = string.IsNullOrWhiteSpace(variantTitle) ? title : $"{title} ({variantTitle})";
        var entity = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = globalProductId,
            ProductId = productId,
            GlobalVariantId = globalVariantId,
            VariantId = variantId,
            ProductTitle = title,
            VariantTitle = variantTitle,
            FullTitle = fullTitle,
            Sku = sku,
            Barcode = barcode
        };

        _dbContext.Set<ShopifyProductVariantEntity>().Add(entity);
        return entity;
    }

    private ShopifyService CreateSut() => new(_shopifyProductService, _dbContext, _logger, _eventAccumulator);

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
