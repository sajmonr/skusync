using Shouldly;
using Web.Api;

namespace Tests.Web.Api;

public class DashboardCorsOptionsTests
{
    [Fact]
    public void GetSanitizedOrigins_TrimsWhitespace()
    {
        var options = new DashboardCorsOptions
        {
            AllowedOrigins = ["  https://skusync.darkflux.app  ", "\thttp://localhost:4200\n"],
        };

        options.GetSanitizedOrigins().ShouldBe(["https://skusync.darkflux.app", "http://localhost:4200"]);
    }

    [Fact]
    public void GetSanitizedOrigins_RemovesEmptyEntries()
    {
        var options = new DashboardCorsOptions
        {
            AllowedOrigins = ["https://skusync.darkflux.app", "", "   "],
        };

        options.GetSanitizedOrigins().ShouldBe(["https://skusync.darkflux.app"]);
    }

    [Fact]
    public void GetSanitizedOrigins_ReturnsEmpty_WhenNoOriginsConfigured()
    {
        var options = new DashboardCorsOptions();

        options.GetSanitizedOrigins().ShouldBeEmpty();
    }
}
