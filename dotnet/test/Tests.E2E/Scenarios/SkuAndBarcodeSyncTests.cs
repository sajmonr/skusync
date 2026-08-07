using Application.Jobs;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Integration.Shopify.Products;
using Integration.Shopify.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class SkuAndBarcodeSyncTests(AppServerTestHost factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ShopifyDispatch_DeactivatesVariant_AfterThreeConsecutiveShopifyRejections()
    {
        // Drift between local Shopify variant and authoritative SkuLabs values. The reconcile
        // mirrors the SkuLabs values into the local row and marks it pending; each dispatch run
        // then tries to push to Shopify and gets rejected with a user error (mirroring the
        // "Product does not exist" case we saw in production logs). After three consecutive push
        // failures the variant should flip to IsActive=false, get a log event, and be silently
        // skipped by the next run — no further Shopify call. The local row stays mirrored and
        // pending throughout.
        const long productId = 9999000000100L;
        const long variantId = 9999000000101L;
        var variantGuid = await SeedDriftedVariantAsync(productId, variantId);

        factory.ShopifyGraphQl
            .ExecuteAsync<UpdateVariantsGraphResponse>(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>>())
            .Returns(new UpdateVariantsGraphResponse(
                [new UserErrorsResponse("Product does not exist", "productId")]));

        await RunReconcileAsync();

        // Run the dispatch three times. Each run finds the same pending variant and fails to
        // push it to Shopify, incrementing FailedShopifySyncAttempts.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await RunShopifyDispatchAsync();
        }

        await factory.ShopifyGraphQl.Received(3).ExecuteAsync<UpdateVariantsGraphResponse>(
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, object?>>());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.ShopifyProductVariants
                .SingleAsync(v => v.ShopifyProductVariantId == variantGuid);

            stored.FailedShopifySyncAttempts.ShouldBe(3);
            stored.IsActive.ShouldBeFalse();
            // Reconcile mirrored the authoritative SkuLabs values locally and marked the row
            // pending, independent of the failing pushes.
            stored.Sku.ShouldBe("skulabs-authoritative");
            stored.Barcode.ShouldBe("skulabs-authoritative-bar");
            stored.PendingShopifySync.ShouldBeTrue();

            var deactivationLog = await db.ShopifyProductVariantLogEvents
                .Where(l => l.ShopifyProductVariantId == variantGuid
                            && l.Message.Contains("deactivated after"))
                .SingleAsync();
            deactivationLog.Message.ShouldBe(
                "Variant deactivated after 3 consecutive failed Shopify sync attempts.");
        }

        factory.ShopifyGraphQl.ClearReceivedCalls();

        await RunShopifyDispatchAsync();

        await factory.ShopifyGraphQl.DidNotReceive().ExecuteAsync<UpdateVariantsGraphResponse>(
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, object?>>());
    }

    private async Task RunReconcileAsync()
    {
        using var scope = factory.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();
        await recurringJobs.ReconcileAll(CancellationToken.None);
    }

    private async Task RunShopifyDispatchAsync()
    {
        using var scope = factory.Services.CreateScope();
        var recurringJobs = scope.ServiceProvider.GetRequiredService<RecurringJobs>();
        await recurringJobs.DispatchShopify(CancellationToken.None);
    }

    private async Task<Guid> SeedDriftedVariantAsync(long productId, long variantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var variant = new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.CreateVersion7(),
            GlobalProductId = $"gid://shopify/Product/{productId}",
            ProductId = productId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            DisplayName = "Drifted Variant",
            Sku = "shopify-old",
            Barcode = "shopify-old-bar"
        };
        db.ShopifyProductVariants.Add(variant);

        db.SkulabsItems.Add(new SkulabsItemEntity
        {
            SkulabsItemId = Guid.CreateVersion7(),
            SkulabsSourceItemId = $"src-{variantId}",
            Title = "Drifted Variant",
            Sku = "skulabs-authoritative",
            Barcode = "skulabs-authoritative-bar",
            Listings =
            {
                new SkulabsItemListingEntity
                {
                    SkulabsSourceListingId = $"lst-{variantId}",
                    RawVariantId = variantId.ToString(),
                    ShopifyProductId = productId.ToString(),
                    ShopifyProductVariantId = variant.ShopifyProductVariantId
                }
            }
        });

        await db.SaveChangesAsync();
        return variant.ShopifyProductVariantId;
    }
}
