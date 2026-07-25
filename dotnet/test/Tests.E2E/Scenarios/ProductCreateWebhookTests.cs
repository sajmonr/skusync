using Infrastructure.Database;
using Integration.Aws.Sqs;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class ProductCreateWebhookTests(AppServerTestHost factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProductsCreateWebhook_PersistsVariant_AndPushesSkuAndBarcodeBackToShopify()
    {
        // arrange — full Shopify webhook envelope with X-Shopify-Topic: products/create
        var envelope = await FixtureLoader.LoadAsync<SqsShopEventProductMessage>(
            "Shopify/Webhooks/products-create-single-variant.json");
        var payload = envelope.Detail.Payload;

        factory.ShopifyGraphQl
            .ExecuteAsync<UpdateVariantsGraphResponse>(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>>())
            .Returns(new UpdateVariantsGraphResponse(null));

        // act — dispatch through the real SqsShopEventProductHandler so topic routing runs
        await factory.DispatchWebhookAsync(envelope);

        // assert — variant persisted with a generated SKU and the variant ID as barcode.
        // The fixture's product title is "Testprod1" (→ "Tes", casing preserved) and the
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
    public async Task ProductsCreateWebhook_AssignsFallbackSku_AndDoesNotPoison_WhenProductTitleIsUnabbreviatable()
    {
        // Regression for #38: an emoji-only title strips to an empty abbreviation. The SKU
        // generator used to throw, the webhook handler propagated it, and SqsShopEventProductHandler
        // returned a non-success status — turning this webhook into a poison message SQS retried
        // forever. It must now degrade to a variant-id-derived SKU and complete successfully.
        var envelope = await FixtureLoader.LoadAsync<SqsShopEventProductMessage>(
            "Shopify/Webhooks/products-create-single-variant.json");
        var poisoned = envelope with
        {
            Detail = envelope.Detail with
            {
                Payload = envelope.Detail.Payload with { Title = "🎁" }
            }
        };
        var variantId = poisoned.Detail.Payload.Variants[0].Id;

        factory.ShopifyGraphQl
            .ExecuteAsync<UpdateVariantsGraphResponse>(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>>())
            .Returns(new UpdateVariantsGraphResponse(null));

        // act — a throw here would mean the handler returned non-success (the poison symptom).
        await Should.NotThrowAsync(() => factory.DispatchWebhookAsync(poisoned));

        // assert — variant persisted with the variant-id fallback SKU (product title contributed
        // nothing; the "Default Title" variant omits the variant segment).
        using var scope = factory.Services.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variant = await dbContext.ShopifyProductVariants
            .SingleAsync(v => v.VariantId == variantId);
        variant.Sku.ShouldBe($"BW-{variantId}");
    }
}
