using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class DashboardAuthenticationTests : IClassFixture<WebApiTestHost>
{
    private readonly WebApiTestHost host;

    public DashboardAuthenticationTests(WebApiTestHost host)
    {
        this.host = host;
    }

    [Fact]
    public async Task DashboardAuthentication_ShouldProtectApiAndManageTheCookieSession()
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        (await client.GetAsync("/status")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/auth/login", new { password = "wrong-password" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await client.PostAsJsonAsync("/auth/login", new { password = "test-password" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/auth/session")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/status")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsync("/auth/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/status")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
