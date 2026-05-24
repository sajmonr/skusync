using Application.Skus;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

/// <summary>
/// Exercises the <see cref="ISkuGenerator"/> against the real Postgres container so the
/// collision-suffix path is covered by the same database engine and EF Core provider used
/// in production. The pure abbreviation rules are covered separately by unit tests against
/// the in-memory provider.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class SkuGenerationCollisionTests(E2EWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Generate_ReturnsBaseSku_WhenNoExistingRowMatches()
    {
        using var scope = factory.Services.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<ISkuGenerator>();

        var sku = await sut.Generate("Basic Tee", "Small / Black");

        sku.ShouldBe("BW-BasTee-SM-BL");
    }

    [Fact]
    public async Task Generate_AppendsDashOne_WhenBaseSkuAlreadyExistsInPostgres()
    {
        await SeedVariantWithSku("BW-BasTee-SM-BL");

        using var scope = factory.Services.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<ISkuGenerator>();

        var sku = await sut.Generate("Basic Tee", "Small / Black");

        sku.ShouldBe("BW-BasTee-SM-BL-1");
    }

    [Fact]
    public async Task Generate_PicksFirstFreeSuffix_WhenMultipleConsecutiveSuffixesExist()
    {
        await SeedVariantWithSku("BW-BasTee-SM-BL");
        await SeedVariantWithSku("BW-BasTee-SM-BL-1");
        await SeedVariantWithSku("BW-BasTee-SM-BL-2");

        using var scope = factory.Services.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<ISkuGenerator>();

        var sku = await sut.Generate("Basic Tee", "Small / Black");

        sku.ShouldBe("BW-BasTee-SM-BL-3");
    }

    private async Task SeedVariantWithSku(string sku)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ShopifyProductVariants.Add(new ShopifyProductVariantEntity
        {
            ShopifyProductVariantId = Guid.CreateVersion7(),
            GlobalProductId = $"gid://shopify/Product/{Random.Shared.Next(1, int.MaxValue)}",
            ProductId = Random.Shared.Next(1, int.MaxValue),
            GlobalVariantId = $"gid://shopify/ProductVariant/{Random.Shared.NextInt64(1, long.MaxValue)}",
            VariantId = Random.Shared.NextInt64(1, long.MaxValue),
            DisplayName = "seed",
            Sku = sku,
            Barcode = $"seed-{Guid.NewGuid():N}"[..16],
        });
        await db.SaveChangesAsync();
    }
}
