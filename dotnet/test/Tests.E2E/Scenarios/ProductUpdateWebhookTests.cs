using System.Text.Json;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class ProductUpdateWebhookTests(AppServerTestHost factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProductsUpdateWebhook_PersistsVariant_AndPushesSkuAndBarcodeBackToShopify_WhenVariantIsNew()
    {
        // arrange — same Shopify webhook envelope as products/create but topic = products/update.
        // The variant is not in our DB, so the update handler should create it and publish a
        // Created event (the update path also creates entities for previously-unknown variants).
        var envelope = await FixtureLoader.LoadAsync<SqsShopEventProductMessage>(
            "Shopify/Webhooks/products-update-single-variant.json");
        var payload = envelope.Detail.Payload;

        factory.ShopifyGraphQl
            .ExecuteAsync<UpdateVariantsGraphResponse>(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>>())
            .Returns(new UpdateVariantsGraphResponse(null));

        // act
        await factory.DispatchWebhookAsync(envelope);

        // assert — variant persisted with a generated SKU and the variant ID as barcode.
        // Fixture's product title is "Testprod1" (→ "Tes", casing preserved) and the
        // variant is the sentinel "Default Title", so the variant segment is omitted.
        var expectedBarcode = payload.Variants[0].Id.ToString();

        using var scope = factory.Services.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variant = await dbContext.ShopifyProductVariants
            .SingleAsync(v => v.VariantId == payload.Variants[0].Id);
        variant.GlobalProductId.ShouldBe(payload.AdminGraphqlApiId);
        variant.GlobalVariantId.ShouldBe(payload.Variants[0].AdminGraphqlApiId);
        variant.Sku.ShouldBe("BW-Tes");
        variant.Barcode.ShouldBe(expectedBarcode);

        // assert — ProductVariantCreatedConsumer fired and called the Shopify GraphQL mutation
        await AsyncWait.UntilAsync(
            () => factory.ShopifyGraphQl.ReceivedCalls().Any(),
            because: "ProductVariantCreatedConsumer should have run and called IShopifyGraphQlService.");

        await factory.ShopifyGraphQl.Received(1).ExecuteAsync<UpdateVariantsGraphResponse>(
            Arg.Is<string>(q => q.Contains("productVariantsBulkUpdate")),
            Arg.Is<IDictionary<string, object?>>(vars =>
                (string)vars["productId"]! == payload.AdminGraphqlApiId));
    }

    [Fact]
    public async Task ProductsUpdateWebhook_PushesOurSkuAndBarcodeBackToShopify_WhenShopifyDriftedFromLocalValues()
    {
        // arrange — we are the source of truth for SKU/barcode. The fixture has sku=null and
        // barcode="" (Shopify drifted). We seed our locally-assigned values so the update
        // handler's diff path fires and the consumer pushes OUR values back to Shopify.
        const long productId = 8521775284385;
        const long variantId = 46450996871329;
        const string ourSku = "OUR-SKU-46450996871329";
        const string ourBarcode = "OUR-BAR-46450996871329";

        using (var seedScope = factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seedDb.ShopifyProductVariants.Add(new ShopifyProductVariantEntity
            {
                GlobalProductId = "gid://shopify/Product/8521775284385",
                ProductId = productId,
                GlobalVariantId = "gid://shopify/ProductVariant/46450996871329",
                VariantId = variantId,
                // Display name matches what ShopifyDisplayName.Compose produces from the fixture
                // (product title "Testprod1" + variant title "Default Title" → "Testprod1"),
                // so the diff path fires solely on the SKU/barcode mismatch.
                DisplayName = "Testprod1",
                Sku = ourSku,
                Barcode = ourBarcode
            });
            await seedDb.SaveChangesAsync();
        }

        var envelope = await FixtureLoader.LoadAsync<SqsShopEventProductMessage>(
            "Shopify/Webhooks/products-update-single-variant.json");

        factory.ShopifyGraphQl
            .ExecuteAsync<UpdateVariantsGraphResponse>(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>>())
            .Returns(new UpdateVariantsGraphResponse(null));

        // act
        await factory.DispatchWebhookAsync(envelope);

        // assert — local SKU/barcode unchanged (we don't accept Shopify's drifted values).
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variant = await db.ShopifyProductVariants.SingleAsync(v => v.VariantId == variantId);
        variant.Sku.ShouldBe(ourSku);
        variant.Barcode.ShouldBe(ourBarcode);

        // assert — ProductVariantUpdatedConsumer fired and the GraphQL call carries OUR
        // SKU and barcode. The "variants" entry is an IEnumerable of anonymous types built
        // by ShopifyProductService.UpdateVariants, so we serialize to JSON to verify content.
        await AsyncWait.UntilAsync(
            () => factory.ShopifyGraphQl.ReceivedCalls().Any(),
            because: "ProductVariantUpdatedConsumer should have run and pushed our SKU/barcode back to Shopify.");

        var capturedVariables = factory.ShopifyGraphQl.ReceivedCalls()
            .Select(c => c.GetArguments()[1] as IDictionary<string, object?>)
            .Single(args => args is not null)!;

        capturedVariables["productId"].ShouldBe("gid://shopify/Product/8521775284385");

        var serializedVariants = JsonSerializer.Serialize(capturedVariables["variants"]);
        serializedVariants.ShouldContain(ourSku);
        serializedVariants.ShouldContain(ourBarcode);
        serializedVariants.ShouldContain("gid://shopify/ProductVariant/46450996871329");
    }
}
