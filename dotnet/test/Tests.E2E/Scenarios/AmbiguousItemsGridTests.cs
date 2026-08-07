using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

/// <summary>
/// Exercises the ambiguous-items grid against real PostgreSQL. The endpoint no longer reads a
/// quarantine table — it derives ambiguity from listing cardinality — so the filter, search and
/// listing-count sort all run over a join that only a relational provider can vouch for.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class AmbiguousItemsGridTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>, IAsyncLifetime
{
    private const long KnownVariantId = 6001L;
    private const long UnknownVariantId = 6999L;

    public Task InitializeAsync() => host.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AmbiguousItemsGrid_ShouldReturnOnlyMultiListingItems_WithTheirListings()
    {
        using var client = await CreateAuthenticatedClient();

        await host.SeedVariant(KnownVariantId, skulabsSourceItemId: null);
        await host.SeedSkulabsItemWithListings("SL-AMBIGUOUS", KnownVariantId, UnknownVariantId);
        await host.SeedSkulabsItemWithListings("SL-CLEAN", KnownVariantId);
        await host.SeedSkulabsItemWithListings("SL-ORPHAN");

        var grid = await GetGrid(client, "/ambiguous-items");

        // Only the two-listing item qualifies; the single-listing and no-listing items do not.
        grid.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = grid.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("skulabsItemId").GetString().ShouldBe("SL-AMBIGUOUS");
        item.GetProperty("listingCount").GetInt32().ShouldBe(2);

        var listings = item.GetProperty("listings").EnumerateArray().ToArray();
        listings.Length.ShouldBe(2);

        // The listing naming a variant we hold resolves; the one naming a variant we have never
        // ingested is still shown, which is usually what explains the ambiguity.
        var resolved = listings.Single(l => l.GetProperty("rawVariantId").GetString() == KnownVariantId.ToString());
        resolved.GetProperty("resolvedToShopifyVariant").GetBoolean().ShouldBeTrue();
        resolved.GetProperty("resolvedVariantId").GetInt64().ShouldBe(KnownVariantId);

        var unresolved = listings.Single(l => l.GetProperty("rawVariantId").GetString() == UnknownVariantId.ToString());
        unresolved.GetProperty("resolvedToShopifyVariant").GetBoolean().ShouldBeFalse();

        // Search reaches the listings as well as the item's own columns.
        var searched = await GetGrid(client, $"/ambiguous-items?search={UnknownVariantId}");
        searched.GetProperty("totalCount").GetInt32().ShouldBe(1);

        var byName = await GetGrid(client, "/ambiguous-items?search=sl-clean");
        byName.GetProperty("totalCount").GetInt32().ShouldBe(0);

        // listingCount is a computed column — sorting on it must translate.
        var sorted = await GetGrid(client, "/ambiguous-items?orderBy=listingCount desc");
        sorted.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    private static async Task<JsonElement> GetGrid(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"GET {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

        (await client.PostAsJsonAsync("/auth/login", new { password = "test-password" }))
            .EnsureSuccessStatusCode();

        return client;
    }
}
