using Gridify;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Web.Api.Common.Paging;

namespace Tests.Web.Api.Common.Paging;

public class QueryablePagingExtensionsTests
{
    [Fact]
    public async Task ToPagedResponseAsync_ShouldUseAliasesAndProjectManually()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Variants.AddRange(
            new VariantEntity { Sku = "A", FailedShopifySyncAttempts = 1 },
            new VariantEntity { Sku = "B", FailedShopifySyncAttempts = 4 },
            new VariantEntity { Sku = "C", FailedShopifySyncAttempts = 2 });
        await dbContext.SaveChangesAsync();

        var query = new GridQuery
        {
            Filter = "failedSyncAttempts>1",
            OrderBy = "failedSyncAttempts desc",
            Page = 1,
            PageSize = 1
        };
        var mapper = new GridifyMapper<VariantEntity>()
            .AddMap("sku", entity => entity.Sku)
            .AddMap("failedSyncAttempts", entity => entity.FailedShopifySyncAttempts);

        var result = await dbContext.Variants.ToPagedResponseAsync(
            query,
            mapper,
            entity => new VariantListItem(entity.Sku, entity.FailedShopifySyncAttempts));

        result.TotalCount.ShouldBe(2);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(1);
        result.Items.ShouldBe([new VariantListItem("B", 4)]);
    }

    private static TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<VariantEntity> Variants => Set<VariantEntity>();
    }

    private sealed class VariantEntity
    {
        public int Id { get; set; }
        public string Sku { get; set; } = "";
        public int FailedShopifySyncAttempts { get; set; }
    }

    private readonly record struct VariantListItem(string Sku, int FailedSyncAttempts);
}
