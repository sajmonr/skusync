using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

/// <summary>
/// Exercises the product-level Shopify lookup the product-details admin extension reads: one request
/// returns every variant of a product, so a single-variant product — which has no variant page at all
/// — is still covered, and a multi-variant one costs one request rather than one page visit each.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ShopifyProductInformationTests : IClassFixture<WebApiTestHost>, IAsyncLifetime
{
    private const long ProductId = 555000111;
    private const long LinkedVariantId = 700000001;
    private const long UnlinkedVariantId = 700000002;
    private const long InactiveVariantId = 700000003;
    private const long DeletedVariantId = 700000004;

    private const long SingleVariantProductId = 555000222;
    private const long SoleVariantId = 700000005;

    private const long DeletedOnlyProductId = 555000333;
    private const long DeletedOnlyVariantId = 700000006;

    private readonly WebApiTestHost _host;

    public ShopifyProductInformationTests(WebApiTestHost host)
    {
        _host = host;
    }

    public async Task InitializeAsync()
    {
        await _host.ResetAsync();

        await _host.SeedVariant(LinkedVariantId, "sl-item-1", productId: ProductId);
        await _host.SeedVariant(UnlinkedVariantId, skulabsSourceItemId: null, productId: ProductId);
        await _host.SeedVariant(InactiveVariantId, "sl-item-3", isActive: false, productId: ProductId);
        await _host.SeedVariant(DeletedVariantId, "sl-item-4", isDeleted: true, productId: ProductId);

        await _host.SeedVariant(SoleVariantId, "sl-item-5", productId: SingleVariantProductId);

        await _host.SeedVariant(
            DeletedOnlyVariantId,
            "sl-item-6",
            isDeleted: true,
            productId: DeletedOnlyProductId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetProductInformation_ShouldReturnEveryVariantOfTheProduct_OrderedBySku()
    {
        var body = await GetProductInformation($"gid://shopify/Product/{ProductId}");

        body.ProductId.ShouldBe(ProductId);
        body.Variants.Select(variant => variant.VariantId)
            .ShouldBe([LinkedVariantId, UnlinkedVariantId, InactiveVariantId]);
    }

    [Fact]
    public async Task GetProductInformation_ShouldReturnTheSkulabsUrl_ForALinkedVariant()
    {
        var body = await GetProductInformation(ProductId.ToString());

        var linked = body.Variants.Single(variant => variant.VariantId == LinkedVariantId);
        linked.Sku.ShouldBe($"SKU-{LinkedVariantId}");
        linked.Title.ShouldBe($"Variant {LinkedVariantId}");
        linked.SkulabsUrl.ShouldBe("https://app.skulabs.com/item?id=sl-item-1");
    }

    [Fact]
    public async Task GetProductInformation_ShouldListAnUnlinkedVariantWithoutAUrl()
    {
        var body = await GetProductInformation(ProductId.ToString());

        var unlinked = body.Variants.Single(variant => variant.VariantId == UnlinkedVariantId);
        unlinked.SkulabsUrl.ShouldBeNull();
    }

    [Fact]
    public async Task GetProductInformation_ShouldStillListADeactivatedVariant()
    {
        var body = await GetProductInformation(ProductId.ToString());

        body.Variants.ShouldContain(variant => variant.VariantId == InactiveVariantId);
    }

    [Fact]
    public async Task GetProductInformation_ShouldExcludeDeletedVariants()
    {
        var body = await GetProductInformation(ProductId.ToString());

        body.Variants.ShouldNotContain(variant => variant.VariantId == DeletedVariantId);
    }

    [Fact]
    public async Task GetProductInformation_ShouldResolveAProductWithASingleVariant()
    {
        var body = await GetProductInformation(SingleVariantProductId.ToString());

        body.Variants.Select(variant => variant.VariantId).ShouldBe([SoleVariantId]);
    }

    [Theory]
    [InlineData(DeletedOnlyProductId)]
    [InlineData(111222333)]
    public async Task GetProductInformation_ShouldReturnNotFound_WhenThereIsNothingToList(long productId)
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/shopify/product-information?productId={productId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("not-a-product")]
    [InlineData("gid://shopify/ProductVariant/700000001")]
    public async Task GetProductInformation_ShouldRejectAnUnparseableProductId(string productId)
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/shopify/product-information?productId={productId}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetProductInformation_ShouldRequireASessionToken()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/shopify/product-information?productId={ProductId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<ProductInformation> GetProductInformation(string productId)
    {
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/shopify/product-information?productId={productId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<ProductInformation>();
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

    private readonly record struct ProductInformation(
        long ProductId,
        IReadOnlyList<ProductVariantInformation> Variants);

    private readonly record struct ProductVariantInformation(
        long VariantId,
        string Sku,
        string Title,
        string? SkulabsUrl);
}
