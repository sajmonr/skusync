using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

/// Exercises SaveChangesToleratingVariantConflicts against the real Postgres container so the
/// unique-constraint race between the maintenance import and the product webhook handlers is
/// covered by the same engine and EF Core provider used in production. The in-memory provider
/// can't reproduce it — it doesn't enforce unique indexes.
[Collection(E2ETestCollection.Name)]
public class VariantConflictTolerantSaveTests(E2EWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SaveChangesToleratingVariantConflicts_DropsAlreadyCommittedInsert_AndPersistsTheRest()
    {
        // A concurrent writer has already committed the variant.
        var committed = NewVariant(globalVariantId: "gid://shopify/ProductVariant/100", variantId: 100, sku: "WON-RACE");
        await CommitVariant(committed);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // This context still thinks both variants are new — it snapshotted before the commit above.
        // The conflicting insert even carries a child log event, which must be detached alongside it.
        var conflicting = NewVariant(globalVariantId: "gid://shopify/ProductVariant/100", variantId: 100, sku: "LOST-RACE");
        conflicting.LogEvents.Add(new ShopifyProductVariantLogEventEntity { Message = "created" });
        var genuinelyNew = NewVariant(globalVariantId: "gid://shopify/ProductVariant/200", variantId: 200, sku: "BRAND-NEW");
        dbContext.ShopifyProductVariants.Add(conflicting);
        dbContext.ShopifyProductVariants.Add(genuinelyNew);

        var dropped = await dbContext.SaveChangesToleratingVariantConflicts(NullLogger.Instance);

        dropped.ShouldHaveSingleItem().ShouldBeSameAs(conflicting);

        using var assertScope = factory.Services.CreateScope();
        var assertDbContext = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rowForRacedVariant = await assertDbContext.ShopifyProductVariants
            .IgnoreQueryFilters()
            .SingleAsync(v => v.VariantId == 100);
        rowForRacedVariant.Sku.ShouldBe("WON-RACE");

        (await assertDbContext.ShopifyProductVariants.IgnoreQueryFilters().AnyAsync(v => v.VariantId == 200))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task SaveChangesToleratingVariantConflicts_Rethrows_WhenConflictIsNotAnAlreadyCommittedInsert()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Two pending inserts share a GlobalVariantId and neither exists in the database, so there
        // is no committed row to defer to — the helper can't resolve it and must surface the error.
        dbContext.ShopifyProductVariants.Add(
            NewVariant(globalVariantId: "gid://shopify/ProductVariant/300", variantId: 300, sku: "A"));
        dbContext.ShopifyProductVariants.Add(
            NewVariant(globalVariantId: "gid://shopify/ProductVariant/300", variantId: 301, sku: "B"));

        await Should.ThrowAsync<DbUpdateException>(
            () => dbContext.SaveChangesToleratingVariantConflicts(NullLogger.Instance));
    }

    private static ShopifyProductVariantEntity NewVariant(string globalVariantId, long variantId, string sku) =>
        new()
        {
            ShopifyProductVariantId = Guid.CreateVersion7(),
            GlobalProductId = $"gid://shopify/Product/{variantId}",
            ProductId = variantId,
            GlobalVariantId = globalVariantId,
            VariantId = variantId,
            DisplayName = "variant",
            Sku = sku,
            Barcode = $"bc-{variantId}"
        };

    private async Task CommitVariant(ShopifyProductVariantEntity variant)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ShopifyProductVariants.Add(variant);
        await dbContext.SaveChangesAsync();
    }
}
