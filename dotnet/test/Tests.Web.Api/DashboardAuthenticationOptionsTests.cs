using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Web.Api;

namespace Tests.Web.Api;

public class DashboardAuthenticationOptionsTests
{
    [Fact]
    public void Validate_ShouldAllowMissingPassword_WhenDevelopmentBypassIsEnabled()
    {
        var options = new DashboardAuthenticationOptions { BypassOnDevelopment = true };

        Should.NotThrow(() => options.Validate(new TestHostEnvironment(Environments.Development)));
    }

    [Fact]
    public void Validate_ShouldRequirePassword_OutsideDevelopmentBypass()
    {
        var options = new DashboardAuthenticationOptions();

        Should.Throw<InvalidOperationException>(() => options.Validate(new TestHostEnvironment(Environments.Staging)));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
