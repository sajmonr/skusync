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
        // The variant is not in our DB, so the update handler should create it with a generated
        // SKU, mark it pending, and immediately dispatch it (the update path also creates
        // entities for previously-unknown variants).
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

        // assert — the immediate dispatch ran inside the webhook flow, pushed the generated SKU
        // via the Shopify GraphQL mutation, and cleared the pending flag.
        variant.PendingShopifySync.ShouldBeFalse();

        await factory.ShopifyGraphQl.Received(1).ExecuteAsync<UpdateVariantsGraphResponse>(
            Arg.Is<string>(q => q.Contains("productVariantsBulkUpdate")),
            Arg.Is<IDictionary<string, object?>>(vars =>
                (string)vars["productId"]! == payload.AdminGraphqlApiId));
    }

    [Fact]
    public async Task ProductsUpdateWebhook_MarksDefaultVariantDeleted_WhenReplacedByRealVariant()
    {
        // arrange — the product originally had only its standalone default variant, which we
        // stored locally. Shopify then created a real variant and dropped the default. The
        // products/update payload carries the real variant (46450996871329) but not the default
        // one we seed here (46450996800000), so the handler must mark the default as deleted.
        const long productId = 8521775284385;
        const long defaultVariantId = 46450996800000;
        Guid defaultVariantGuid;

        using (var seedScope = factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var defaultVariant = new ShopifyProductVariantEntity
            {
                GlobalProductId = "gid://shopify/Product/8521775284385",
                ProductId = productId,
                GlobalVariantId = "gid://shopify/ProductVariant/46450996800000",
                VariantId = defaultVariantId,
                DisplayName = "Testprod1",
                Sku = "OLD-DEFAULT-SKU",
                Barcode = ""
            };
            defaultVariant.LogEvents.Add(new ShopifyProductVariantLogEventEntity
            {
                Message = "Product variant was created."
            });
            seedDb.ShopifyProductVariants.Add(defaultVariant);
            await seedDb.SaveChangesAsync();
            defaultVariantGuid = defaultVariant.ShopifyProductVariantId;
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

        // assert
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The default variant is preserved (not physically removed) and marked terminally deleted.
        var deleted = await db.ShopifyProductVariants.SingleAsync(v => v.VariantId == defaultVariantId);
        deleted.IsDeleted.ShouldBeTrue();
        deleted.DeletedOn.ShouldBeGreaterThan(DateTime.MinValue);

        // The real variant from the payload is tracked and live.
        var real = await db.ShopifyProductVariants.SingleAsync(v => v.VariantId == 46450996871329);
        real.IsDeleted.ShouldBeFalse();

        // Audit log is preserved: the original creation event survives alongside the new deletion event.
        var logMessages = await db.ShopifyProductVariantLogEvents
            .Where(e => e.ShopifyProductVariantId == defaultVariantGuid)
            .Select(e => e.Message)
            .ToListAsync();
        logMessages.ShouldContain(m => m.Contains("created"));
        logMessages.ShouldContain(m => m.Contains("deleted"));
    }

    [Fact]
    public async Task ProductsUpdateWebhook_PushesOurSkuAndBarcodeBackToShopify_WhenShopifyDriftedFromLocalValues()
    {
        // arrange — we are the source of truth for SKU/barcode. The fixture has sku=null and
        // barcode="" (Shopify drifted). We seed our locally-assigned values so the update
        // handler's diff path fires, marks the variant pending, and the immediate dispatch
        // pushes OUR values back to Shopify.
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

        // assert — the immediate dispatch ran inside the webhook flow and the GraphQL call
        // carries OUR SKU and barcode. The "variants" entry is an IEnumerable of anonymous types
        // built by ShopifyProductService.UpdateVariants, so we serialize to JSON to verify content.
        var capturedVariables = factory.ShopifyGraphQl.ReceivedCalls()
            .Select(c => c.GetArguments()[1] as IDictionary<string, object?>)
            .Single(args => args is not null)!;

        capturedVariables["productId"].ShouldBe("gid://shopify/Product/8521775284385");

        var serializedVariants = JsonSerializer.Serialize(capturedVariables["variants"]);
        serializedVariants.ShouldContain(ourSku);
        serializedVariants.ShouldContain(ourBarcode);
        serializedVariants.ShouldContain("gid://shopify/ProductVariant/46450996871329");
    }

    [Fact]
    public async Task ProductsUpdateWebhook_AssignsFallbackSku_AndDoesNotPoison_WhenNewVariantTitleIsUnabbreviatable()
    {
        // Regression for #38: an emoji-only title strips to an empty abbreviation. When the update
        // handler creates the previously-unknown variant, the SKU generator used to throw, the
        // handler propagated it, and SqsShopEventProductHandler returned a non-success status —
        // turning this webhook into a poison message SQS retried forever. It must now degrade to a
        // variant-id-derived SKU and complete successfully.
        var envelope = await FixtureLoader.LoadAsync<SqsShopEventProductMessage>(
            "Shopify/Webhooks/products-update-single-variant.json");
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
        await using var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variant = await db.ShopifyProductVariants.SingleAsync(v => v.VariantId == variantId);
        variant.Sku.ShouldBe($"BW-{variantId}");
    }
}
