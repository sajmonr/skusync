using Shouldly;
using Web.Api.Shopify.Authentication;

namespace Tests.Web.Api.Shopify;

public class ShopifyShopDomainTests
{
    [Theory]
    [InlineData("https://Ivy.myshopify.com/", "https://ivy.myshopify.com")]
    [InlineData("  https://ivy.myshopify.com  ", "https://ivy.myshopify.com")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_StripsCasingWhitespaceAndTrailingSlash(string? shopUrl, string expected)
    {
        ShopifyShopDomain.Normalize(shopUrl).ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://ivy.myshopify.com", "https://ivy.myshopify.com")]
    [InlineData("https://IVY.myshopify.com/", "https://ivy.myshopify.com")]
    public void Matches_IgnoresCasingAndTrailingSlash(string candidate, string expected)
    {
        ShopifyShopDomain.Matches(candidate, expected).ShouldBeTrue();
    }

    [Fact]
    public void Matches_ReturnsFalse_WhenShopsDiffer()
    {
        ShopifyShopDomain.Matches("https://attacker.myshopify.com", "https://ivy.myshopify.com")
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, "https://ivy.myshopify.com")]
    [InlineData("", "https://ivy.myshopify.com")]
    [InlineData("https://ivy.myshopify.com", null)]
    [InlineData("https://ivy.myshopify.com", "")]
    [InlineData(null, null)]
    public void Matches_FailsClosed_WhenEitherSideIsMissing(string? candidate, string? expected)
    {
        ShopifyShopDomain.Matches(candidate, expected).ShouldBeFalse();
    }

    [Fact]
    public void CreateIssuer_AppendsTheAdminPath()
    {
        ShopifyShopDomain.CreateIssuer("https://Ivy.myshopify.com/")
            .ShouldBe("https://ivy.myshopify.com/admin");
    }
}
