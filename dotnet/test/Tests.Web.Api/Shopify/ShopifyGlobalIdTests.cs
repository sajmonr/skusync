using Shouldly;
using Web.Api.Shopify;

namespace Tests.Web.Api.Shopify;

public class ShopifyGlobalIdTests
{
    [Theory]
    [InlineData("gid://shopify/ProductVariant/987654321")]
    [InlineData("GID://SHOPIFY/PRODUCTVARIANT/987654321")]
    [InlineData("  gid://shopify/ProductVariant/987654321  ")]
    [InlineData("987654321")]
    public void TryParseVariantId_AcceptsGlobalIdsAndBareNumbers(string value)
    {
        var parsed = ShopifyGlobalId.TryParseVariantId(value, out var variantId);

        parsed.ShouldBeTrue();
        variantId.ShouldBe(987654321);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("gid://shopify/ProductVariant/")]
    [InlineData("gid://shopify/ProductVariant/abc")]
    [InlineData("gid://shopify/Product/987654321")]
    public void TryParseVariantId_RejectsUnparseableValues(string? value)
    {
        var parsed = ShopifyGlobalId.TryParseVariantId(value, out var variantId);

        parsed.ShouldBeFalse();
        variantId.ShouldBe(0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("gid://shopify/ProductVariant/0")]
    public void TryParseVariantId_RejectsNonPositiveIds(string value)
    {
        ShopifyGlobalId.TryParseVariantId(value, out _).ShouldBeFalse();
    }
}
