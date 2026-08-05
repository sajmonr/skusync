using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Web.Api.Shopify.Authentication;

namespace Tests.Web.Api.Shopify;

public class ShopifyAppOptionsTests
{
    private const string ShopUrl = "https://ivy.myshopify.com";

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenBothCredentialsArePresent()
    {
        CreateOptions("client-id", "client-secret").IsConfigured.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "client-secret")]
    [InlineData("client-id", "")]
    [InlineData("   ", "client-secret")]
    [InlineData("client-id", "   ")]
    [InlineData("", "")]
    public void IsConfigured_ReturnsFalse_WhenEitherCredentialIsMissing(string clientId, string clientSecret)
    {
        CreateOptions(clientId, clientSecret).IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenCredentialsAreMissingInDevelopment()
    {
        var options = CreateOptions("", "");

        options.Validate(CreateEnvironment(Environments.Development), ShopUrl);
    }

    [Fact]
    public void Validate_Throws_WhenCredentialsAreMissingOutsideDevelopment()
    {
        var options = CreateOptions("", "");

        var exception = Should.Throw<InvalidOperationException>(
            () => options.Validate(CreateEnvironment(Environments.Production), ShopUrl));

        exception.Message.ShouldContain(ShopifyAppOptions.SectionKey);
    }

    [Fact]
    public void Validate_Throws_WhenShopUrlIsMissingOutsideDevelopment()
    {
        var options = CreateOptions("client-id", "client-secret");

        var exception = Should.Throw<InvalidOperationException>(
            () => options.Validate(CreateEnvironment(Environments.Production), ""));

        exception.Message.ShouldContain("ShopUrl");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenFullyConfiguredOutsideDevelopment()
    {
        var options = CreateOptions("client-id", "client-secret");

        options.Validate(CreateEnvironment(Environments.Production), ShopUrl);
    }

    private static ShopifyAppOptions CreateOptions(string clientId, string clientSecret) =>
        new() { ClientId = clientId, ClientSecret = clientSecret };

    private static IHostEnvironment CreateEnvironment(string environmentName) =>
        new TestHostEnvironment(environmentName);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
