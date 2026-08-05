using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

/// <summary>
/// Exercises the Shopify surface end to end: a session token minted the way Shopify mints one is
/// validated by the real authentication scheme, and the endpoint resolves a seeded variant to its
/// SkuLabs admin URL.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ShopifyVariantInformationTests : IClassFixture<WebApiTestHost>, IAsyncLifetime
{
    private const long LinkedVariantId = 987654321;
    private const long UnlinkedVariantId = 987654322;
    private const long DeletedVariantId = 987654323;
    private const long InactiveVariantId = 987654324;
    private const string SkulabsItemId = "sl-item-1";

    private readonly WebApiTestHost _host;

    public ShopifyVariantInformationTests(WebApiTestHost host)
    {
        _host = host;
    }

    public async Task InitializeAsync()
    {
        await _host.ResetAsync();

        await _host.SeedVariant(LinkedVariantId, SkulabsItemId);
        await _host.SeedVariant(UnlinkedVariantId, skulabsSourceItemId: null);
        await _host.SeedVariant(DeletedVariantId, "sl-item-deleted", isDeleted: true);
        await _host.SeedVariant(InactiveVariantId, "sl-item-inactive", isActive: false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetVariantInformation_ShouldReturnTheSkulabsUrl_ForALinkedVariant()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/shopify/variant-information?variantId=gid://shopify/ProductVariant/{LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<VariantInformation>();
        body.VariantId.ShouldBe(LinkedVariantId);
        body.SkulabsItemId.ShouldBe(SkulabsItemId);
        body.SkulabsUrl.ShouldBe($"https://app.skulabs.com/item?id={SkulabsItemId}");
    }

    [Fact]
    public async Task GetVariantInformation_ShouldAcceptABareNumericVariantId()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/shopify/variant-information?variantId={LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldStillResolve_ForADeactivatedVariant()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/shopify/variant-information?variantId={InactiveVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(UnlinkedVariantId)]
    [InlineData(DeletedVariantId)]
    [InlineData(111222333)]
    public async Task GetVariantInformation_ShouldReturnNotFound_WhenThereIsNoLinkedSkulabsItem(long variantId)
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/shopify/variant-information?variantId={variantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldRejectAnUnparseableVariantId()
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/shopify/variant-information?variantId=not-a-variant");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldRequireASessionToken()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/shopify/variant-information?variantId={LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldRejectATokenSignedWithTheWrongSecret()
    {
        using var client = CreateClient(_host.CreateSessionToken(signingSecret: "a-different-secret-of-sufficient-length"));

        var response = await client.GetAsync($"/shopify/variant-information?variantId={LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldRejectATokenIssuedForAnotherShop()
    {
        using var client = CreateClient(_host.CreateSessionToken(shop: "https://someone-else.myshopify.com"));

        var response = await client.GetAsync($"/shopify/variant-information?variantId={LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldRejectATokenIssuedForAnotherApp()
    {
        using var client = CreateClient(_host.CreateSessionToken(clientId: "some-other-app"));

        var response = await client.GetAsync($"/shopify/variant-information?variantId={LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldRejectAnExpiredToken()
    {
        using var client = CreateClient(_host.CreateSessionToken(lifetime: TimeSpan.FromMinutes(-10)));

        var response = await client.GetAsync($"/shopify/variant-information?variantId={LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVariantInformation_ShouldRejectADashboardCookieSession()
    {
        using var client = _host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        (await client.PostAsJsonAsync("/auth/login", new { password = "test-password" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await client.GetAsync($"/shopify/variant-information?variantId={LinkedVariantId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ShopifyEndpoints_ShouldAllowTheExtensionSandboxOrigin()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            $"/shopify/variant-information?variantId={LinkedVariantId}");
        request.Headers.Add("Origin", "https://extensions.shopifycdn.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin")
            .ShouldContain("https://extensions.shopifycdn.com");
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task ShopifyEndpoints_ShouldRejectAnUnknownOrigin()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            $"/shopify/variant-information?variantId={LinkedVariantId}");
        request.Headers.Add("Origin", "https://not-shopify.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private HttpClient CreateAuthenticatedClient() => CreateClient(_host.CreateSessionToken());

    private HttpClient CreateClient(string? sessionToken = null)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        if (sessionToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        }

        return client;
    }

    private readonly record struct VariantInformation(
        long VariantId,
        string SkulabsItemId,
        string SkulabsUrl);
}
