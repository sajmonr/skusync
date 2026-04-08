using Integration.Shopify.Products;
using Shouldly;

namespace Tests.Integration.Shopify.Products;

public class ShopifyProductVariantTests
{
    // -------------------------------------------------------------------------
    // ProductId parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void ProductId_ShouldParseNumericSegment_FromValidGlobalId()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: "gid://shopify/Product/123456789",
            GlobalVariantId: "gid://shopify/ProductVariant/1",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.ProductId.ShouldBe(123456789L);
    }

    [Fact]
    public void ProductId_ShouldReturnZero_WhenSegmentAfterLastSlashIsNotNumeric()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: "gid://shopify/Product/abc",
            GlobalVariantId: "gid://shopify/ProductVariant/1",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.ProductId.ShouldBe(0L);
    }

    [Fact]
    public void ProductId_ShouldReturnZero_WhenGlobalIdIsEmpty()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: "",
            GlobalVariantId: "gid://shopify/ProductVariant/1",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.ProductId.ShouldBe(0L);
    }

    [Fact]
    public void ProductId_ShouldReturnZero_WhenSegmentAfterLastSlashIsEmpty()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: "gid://shopify/Product/",
            GlobalVariantId: "gid://shopify/ProductVariant/1",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.ProductId.ShouldBe(0L);
    }

    // -------------------------------------------------------------------------
    // VariantId parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void VariantId_ShouldParseNumericSegment_FromValidGlobalId()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: "gid://shopify/Product/1",
            GlobalVariantId: "gid://shopify/ProductVariant/987654321",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.VariantId.ShouldBe(987654321L);
    }

    [Fact]
    public void VariantId_ShouldReturnZero_WhenSegmentAfterLastSlashIsNotNumeric()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: "gid://shopify/Product/1",
            GlobalVariantId: "gid://shopify/ProductVariant/xyz",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.VariantId.ShouldBe(0L);
    }

    [Fact]
    public void VariantId_ShouldReturnZero_WhenGlobalIdIsEmpty()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: "gid://shopify/Product/1",
            GlobalVariantId: "",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.VariantId.ShouldBe(0L);
    }

    // -------------------------------------------------------------------------
    // Large IDs (near long.MaxValue)
    // -------------------------------------------------------------------------

    [Fact]
    public void ProductId_ShouldHandleLargeIds_WithoutOverflow()
    {
        var variant = new ShopifyProductVariant(
            GlobalProductId: $"gid://shopify/Product/{long.MaxValue}",
            GlobalVariantId: "gid://shopify/ProductVariant/1",
            ProductTitle: "", VariantTitle: "", Sku: "", Barcode: "");

        variant.ProductId.ShouldBe(long.MaxValue);
    }
}
