using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class ProductDeleteWebhookTests(AppServerTestHost factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProductsDeleteWebhook_MarksAllStoredVariantsDeleted_WhenProductDeletedInShopify()
    {
        // arrange — the deleted product's two variants are tracked locally and live. The
        // products/delete payload carries only the product id, so removal is driven off ProductId.
        const long productId = 8521775284385;
        const long firstVariantId = 46450996871329;
        const long secondVariantId = 46450996800000;
        Guid firstVariantGuid;

        using (var seedScope = factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = SeedVariant(productId, firstVariantId);
            var second = SeedVariant(productId, secondVariantId);
            seedDb.ShopifyProductVariants.Add(first);
            seedDb.ShopifyProductVariants.Add(second);
            await seedDb.SaveChangesAsync();
            firstVariantGuid = first.ShopifyProductVariantId;
        }

        var envelope = await FixtureLoader.LoadAsync<SqsShopEventProductMessage>(
            "Shopify/Webhooks/products-delete-single-variant.json");

        // act — dispatch through the real SqsShopEventProductHandler so topic routing runs
        await factory.DispatchWebhookAsync(envelope);

        // assert — both variants are preserved (not physically removed) and terminally deleted,
        // with IsActive untouched.
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var variants = await db.ShopifyProductVariants
            .Where(v => v.ProductId == productId)
            .ToListAsync();
        variants.Count.ShouldBe(2);
        variants.ShouldAllBe(v => v.IsDeleted);
        variants.ShouldAllBe(v => v.DeletedOn > DateTime.MinValue);
        variants.ShouldAllBe(v => v.IsActive);

        // A deletion audit event was written for the variant.
        var logMessages = await db.ShopifyProductVariantLogEvents
            .Where(e => e.ShopifyProductVariantId == firstVariantGuid)
            .Select(e => e.Message)
            .ToListAsync();
        logMessages.ShouldContain(m => m.Contains("deleted"));
    }

    private static ShopifyProductVariantEntity SeedVariant(long productId, long variantId) =>
        new()
        {
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = "Testprod1",
            Sku = $"SKU-{variantId}",
            Barcode = ""
        };
}
