using Application.Skus;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Tests.Application.Skus;

public class SkuGeneratorTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public SkuGeneratorTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);
    }

    public void Dispose() => _dbContext.Dispose();

    // -------------------------------------------------------------------------
    // Composition — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Generate_UsesPrefix_AndProductAndVariantSegments()
    {
        var sut = CreateSut();
        var sku = await sut.Generate("Basic Tee", "Small / Black");
        sku.ShouldBe("BW-BasTee-SM-BL");
    }

    [Fact]
    public async Task Generate_OmitsVariantSegment_ForDefaultTitleVariant()
    {
        var sut = CreateSut();
        var sku = await sut.Generate("Basic Tee", "Default Title");
        sku.ShouldBe("BW-BasTee");
    }

    [Fact]
    public async Task Generate_OmitsVariantSegment_ForNullOrEmptyVariantTitle()
    {
        var sut = CreateSut();
        (await sut.Generate("Basic Tee", null)).ShouldBe("BW-BasTee");
        (await sut.Generate("Basic Tee", "")).ShouldBe("BW-BasTee");
        (await sut.Generate("Basic Tee", "   ")).ShouldBe("BW-BasTee");
    }

    // -------------------------------------------------------------------------
    // Product-title abbreviation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Generate_TakesFirstThreeCharsOfEachWord_FromProductTitle()
    {
        var sut = CreateSut();
        // "New Awesome Tshirt" → New + Awe + Tsh (casing preserved on product segment)
        var sku = await sut.Generate("New Awesome Tshirt", "Default Title");
        sku.ShouldBe("BW-NewAweTsh");
    }

    [Fact]
    public async Task Generate_StripsPunctuation_FromProductWords()
    {
        var sut = CreateSut();
        // "T-Shirt" is a single token with a hyphen; alphanumeric stripping yields "TShirt" → "TSh"
        var sku = await sut.Generate("T-Shirt", "Default Title");
        sku.ShouldBe("BW-TSh");
    }

    [Fact]
    public async Task Generate_CapitalizesFirstCharOfEachProductWord_AndUpperCasesVariantSegments()
    {
        var sut = CreateSut();
        // First char of each product word is upper-cased ("basic" → "Bas", "tee" → "Tee");
        // remaining chars preserve their original casing. Variant segments are always
        // upper-cased regardless of input casing.
        var sku = await sut.Generate("basic tee", "small / black");
        sku.ShouldBe("BW-BasTee-SM-BL");
    }

    // -------------------------------------------------------------------------
    // Variant-title abbreviation: size dictionary + 2-char fallback
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Small", "SM")]
    [InlineData("Medium", "MD")]
    [InlineData("Large", "LG")]
    [InlineData("XL", "XL")]
    [InlineData("Extra Large", "XL")]
    [InlineData("XXL", "2XL")]
    [InlineData("2XL", "2XL")]
    [InlineData("XXXL", "3XL")]
    [InlineData("XS", "XS")]
    public async Task Generate_UsesCanonicalSizeAbbreviations(string sizeWord, string expectedAbbrev)
    {
        var sut = CreateSut();
        var sku = await sut.Generate("Tee", sizeWord);
        sku.ShouldBe($"BW-Tee-{expectedAbbrev}");
    }

    [Fact]
    public async Task Generate_TakesFirstTwoCharsOfVariantWord_WhenNotASize()
    {
        var sut = CreateSut();
        var sku = await sut.Generate("Tee", "Cotton / Green");
        sku.ShouldBe("BW-Tee-CO-GR");
    }

    [Fact]
    public async Task Generate_HandlesMixedSizeAndColorParts()
    {
        var sut = CreateSut();
        var sku = await sut.Generate("Tee", "Large / Blue");
        sku.ShouldBe("BW-Tee-LG-BL");
    }

    // -------------------------------------------------------------------------
    // Max-length truncation: only the product abbreviation shrinks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Generate_TruncatesProductAbbreviation_FromTheEnd_ToFitMaxLength()
    {
        // Mirrors the example in the README: MaxLength=15, prefix=BW, delim=-
        // Product "New Awesome Tshirt Made By Boho WanderLust" + "Small / Green"
        // Full would be BW-NewAweTshMadByBohWan-SM-GR (way over 15).
        // With suffix=0: available for product = 15 - 2 - 1 - 1 - 2 - 1 - 2 = 6 → NewAwe
        var sut = CreateSut(maxLength: 15);
        var sku = await sut.Generate("New Awesome Tshirt Made By Boho WanderLust", "Small / Green");
        sku.ShouldBe("BW-NewAwe-SM-GR");
        sku.Length.ShouldBeLessThanOrEqualTo(15);
    }

    [Fact]
    public async Task Generate_NeverExceedsMaxLength_EvenWithCollisionSuffix()
    {
        // Pre-seed every short candidate so the generator is forced to add a suffix and
        // shrink the product abbreviation further to fit.
        Seed("BW-NewAwe-SM-GR");
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut(maxLength: 15);
        var sku = await sut.Generate("New Awesome Tshirt Made By Boho WanderLust", "Small / Green");
        sku.Length.ShouldBeLessThanOrEqualTo(15);
        sku.ShouldEndWith("-SM-GR-1");
        sku.ShouldStartWith("BW-");
    }

    [Fact]
    public async Task Generate_ThrowsDescriptive_WhenMaxLengthCannotFitEvenOneCharProduct()
    {
        // Prefix(2) + delim(1) + product(1) + delim(1) + variant(2) + delim(1) + variant(2) = 10
        // MaxLength=9 leaves negative space for product → throw.
        var sut = CreateSut(maxLength: 9);
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => sut.Generate("Anything", "Small / Green"));
        exception.Message.ShouldContain("MaxLength");
    }

    // -------------------------------------------------------------------------
    // Collision suffix
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Generate_AppendsDashOne_WhenBaseSkuCollidesInDatabase()
    {
        Seed("BW-BasTee-SM-BL");
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();
        var sku = await sut.Generate("Basic Tee", "Small / Black");
        sku.ShouldBe("BW-BasTee-SM-BL-1");
    }

    [Fact]
    public async Task Generate_IncrementsSuffix_UntilOneIsFree()
    {
        Seed("BW-BasTee-SM-BL");
        Seed("BW-BasTee-SM-BL-1");
        Seed("BW-BasTee-SM-BL-2");
        await _dbContext.SaveChangesAsync();

        var sut = CreateSut();
        var sku = await sut.Generate("Basic Tee", "Small / Black");
        sku.ShouldBe("BW-BasTee-SM-BL-3");
    }

    [Fact]
    public async Task Generate_AvoidsCollisions_WithSkusReservedInTheCurrentBatch()
    {
        var sut = CreateSut();
        var reserved = new HashSet<string>(StringComparer.Ordinal) { "BW-BasTee-SM-BL" };
        var sku = await sut.Generate("Basic Tee", "Small / Black", reserved);
        sku.ShouldBe("BW-BasTee-SM-BL-1");
    }

    // -------------------------------------------------------------------------
    // Configuration: prefix and delimiter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Generate_UsesConfiguredPrefix_WhenOverridden()
    {
        var sut = CreateSut(prefix: "ACME");
        var sku = await sut.Generate("Basic Tee", "Small / Black");
        sku.ShouldBe("ACME-BasTee-SM-BL");
    }

    [Fact]
    public async Task Generate_UsesConfiguredDelimiter_WhenOverridden()
    {
        var sut = CreateSut(delimiter: "_");
        var sku = await sut.Generate("Basic Tee", "Small / Black");
        sku.ShouldBe("BW_BasTee_SM_BL");
    }

    // -------------------------------------------------------------------------
    // Empty / invalid input
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Generate_Throws_WhenProductTitleIsEmpty()
    {
        var sut = CreateSut();
        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.Generate("", "Small / Black"));
    }

    [Fact]
    public async Task Generate_Throws_WhenProductTitleHasNoAlphanumericChars()
    {
        // Punctuation-only / whitespace title strips down to nothing → no product abbrev
        // → generator can't build a meaningful SKU and must surface the configuration error.
        var sut = CreateSut();
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => sut.Generate("--- !!! ---", "Small / Black"));
        exception.Message.ShouldContain("empty abbreviation");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private SkuGenerator CreateSut(string prefix = "BW", int maxLength = 25, string delimiter = "-")
    {
        var opts = Options.Create(new SkuGeneratorOptions
        {
            Prefix = prefix,
            MaxLength = maxLength,
            Delimiter = delimiter,
        });
        return new SkuGenerator(_dbContext, opts, NullLogger<SkuGenerator>.Instance);
    }

    private void Seed(string sku)
    {
        _dbContext.ShopifyProductVariants.Add(new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.NewGuid(),
            GlobalProductId = $"gid://shopify/Product/{Random.Shared.Next(1, int.MaxValue)}",
            ProductId = Random.Shared.Next(1, int.MaxValue),
            GlobalVariantId = $"gid://shopify/ProductVariant/{Random.Shared.Next(1, int.MaxValue)}",
            VariantId = Random.Shared.NextInt64(1, long.MaxValue),
            DisplayName = "seed",
            Sku = sku,
            Barcode = "seed",
        });
    }
}
