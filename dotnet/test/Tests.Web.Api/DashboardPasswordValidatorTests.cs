using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Web.Api;

namespace Tests.Web.Api;

public class DashboardPasswordValidatorTests
{
    [Theory]
    [InlineData("correct-password", true)]
    [InlineData("wrong-password", false)]
    [InlineData("", false)]
    public void IsValid_ShouldRequireTheConfiguredPassword_OutsideDevelopmentBypass(
        string password,
        bool expected)
    {
        var sut = new DashboardPasswordValidator(
            new DashboardAuthenticationOptions { Password = "correct-password" },
            new TestHostEnvironment(Environments.Staging));

        sut.IsValid(password).ShouldBe(expected);
    }

    [Fact]
    public void IsValid_ShouldAcceptAnyPassword_WhenDevelopmentBypassIsEnabled()
    {
        var sut = new DashboardPasswordValidator(
            new DashboardAuthenticationOptions { BypassOnDevelopment = true },
            new TestHostEnvironment(Environments.Development));

        sut.IsValid("anything").ShouldBeTrue();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
