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
public class ProductUpdateWebhookTests(E2EWebApplicationFactory factory) : IAsyncLifetime
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

        // assert — variant persisted with VariantId as initial SKU and Barcode
        var expectedSku = payload.Variants[0].Id.ToString();
        var expectedBarcode = payload.Variants[0].Id.ToString();

        using var scope = factory.Services.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variant = await dbContext.ShopifyProductVariants
            .SingleAsync(v => v.VariantId == payload.Variants[0].Id);
        variant.GlobalProductId.ShouldBe(payload.AdminGraphqlApiId);
        variant.GlobalVariantId.ShouldBe(payload.Variants[0].AdminGraphqlApiId);
        variant.Sku.ShouldBe(expectedSku);
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
}
