using Application.Products.Services;
using Application.Products.Webhook;
using Infrastructure.Database;
using Integration.Aws.Sqs;
using Integration.Shopify.GraphQl;
using Integration.Shopify.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

public class ProductCreateWebhookTests(E2EWebApplicationFactory factory)
    : IClassFixture<E2EWebApplicationFactory>
{
    [Fact]
    public async Task ProductsCreateWebhook_PersistsVariant_AndPushesSkuAndBarcodeBackToShopify()
    {
        // arrange
        var payload = await FixtureLoader.LoadAsync<SqsShopEventProduct>(
            "Shopify/Webhooks/products-create-single-variant.json");

        factory.ShopifyGraphQl
            .ExecuteAsync<UpdateVariantsGraphResponse>(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>>())
            .Returns(new UpdateVariantsGraphResponse(null));

        using var scope = factory.Services.CreateScope();
        var createHandler = scope.ServiceProvider.GetServices<IShopifyWebhookHandler>()
            .Single(h => h.TopicName == "products/create");

        // act
        await createHandler.Handle(payload);

        // assert — variant persisted with VariantId as initial SKU and Barcode
        var expectedSku = payload.Variants[0].Id.ToString();
        var expectedBarcode = payload.Variants[0].Id.ToString();

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
