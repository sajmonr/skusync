using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

/// <summary>
/// Exercises the item-sync grid against real PostgreSQL. The resolved-SkuLabs-item join is filtered,
/// searched and sorted on, and those are the shapes most likely to translate differently from the
/// in-memory provider the unit tests use — so they need a relational run to be believed.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ItemSyncGridTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>, IAsyncLifetime
{
    private const long LinkedVariantId = 5001L;
    private const long UnlinkedVariantId = 5002L;
    private const long ContestedVariantId = 5003L;

    public Task InitializeAsync() => host.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ItemSyncGrid_ShouldFilterSearchAndSort_OverTheResolvedSkulabsItem()
    {
        using var client = await CreateAuthenticatedClient();

        await host.SeedVariant(LinkedVariantId, skulabsSourceItemId: "SL-LINKED");
        await host.SeedVariant(UnlinkedVariantId, skulabsSourceItemId: null);
        await host.SeedVariant(ContestedVariantId, skulabsSourceItemId: "SL-FIRST");
        await host.SeedCompetingSkulabsItem(ContestedVariantId, "SL-SECOND");

        // Every variant is listed, whatever its SkuLabs state.
        var all = await GetGrid(client, "/item-sync?orderBy=displayName");
        all.GetProperty("totalCount").GetInt32().ShouldBe(3);

        // The linked variant's SkuLabs title differs from its display name, so it reads as drifted.
        var outOfSync = await GetGrid(client, "/item-sync?status=out-of-sync");
        outOfSync.GetProperty("totalCount").GetInt32().ShouldBe(1);
        VariantIds(outOfSync).ShouldBe([LinkedVariantId]);

        // A variant two items are fighting over has no item we can act on, so it reads the same as
        // one with no SkuLabs item at all.
        var missing = await GetGrid(client, "/item-sync?status=missing-in-skulabs");
        VariantIds(missing).ShouldBe([UnlinkedVariantId, ContestedVariantId], ignoreOrder: true);

        // Searching reaches across into the SkuLabs side of the join.
        var searched = await GetGrid(client, "/item-sync?search=sl-linked");
        searched.GetProperty("totalCount").GetInt32().ShouldBe(1);
        VariantIds(searched).ShouldBe([LinkedVariantId]);

        // Sorting on a SkuLabs column is a sort over the joined row, not the variant table.
        var sorted = await GetGrid(client, "/item-sync?orderBy=skulabsTitle desc");
        sorted.GetProperty("totalCount").GetInt32().ShouldBe(3);
    }

    private static long[] VariantIds(JsonElement grid) =>
        grid.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("shopifyId").GetInt64())
            .ToArray();

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
