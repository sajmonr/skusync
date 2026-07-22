using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Web.Api.Common.Paging;
using Web.Api.Features.ProductVariants.GetProductVariants;

namespace Tests.Web.Api.Features.ProductVariants;

public class GetProductVariantsTests
{
    [Fact]
    public async Task Query_ShouldFilterUsingPublicAliasesAndProjectTheResponse()
    {
        await using var dbContext = CreateDbContext();
        var visibleVariant = CreateVariant("Blue shirt", "SHIRT-BLUE", "100", 3);
        dbContext.ShopifyProductVariants.AddRange(
            visibleVariant,
            CreateVariant("Red shirt", "SHIRT-RED", "200", 0),
            CreateVariant("Green pants", "PANTS-GREEN", "300", 2));
        await dbContext.SaveChangesAsync();

        var query = new GetProductVariantsRequest
        {
            Filter = "sku=*shirt/i,failedSyncAttempts>1",
            OrderBy = "failedSyncAttempts desc"
        };

        var result = await dbContext.ShopifyProductVariants
            .AsNoTracking()
            .ToPagedResponseAsync(
                query,
                ProductVariantGridMapper.Instance,
                ProductVariantListItem.Projection);

        result.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem();
        result.Items[0].ShouldBe(new ProductVariantListItem(
            visibleVariant.ShopifyProductVariantId,
            visibleVariant.ProductId,
            visibleVariant.VariantId,
            visibleVariant.DisplayName,
            visibleVariant.Sku,
            visibleVariant.Barcode,
            visibleVariant.PendingShopifySync,
            visibleVariant.FailedShopifySyncAttempts,
            visibleVariant.IsActive,
            visibleVariant.UpdatedOnUtc));
    }

    [Fact]
    public void Validator_ShouldRejectInfrastructurePropertyNames()
    {
        var request = new GetProductVariantsRequest
        {
            Filter = "FailedShopifySyncAttempts>0"
        };

        var result = new GetProductVariantsRequestValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == "query");
    }

    [Fact]
    public void Validator_ShouldAcceptPublicAliases()
    {
        var request = new GetProductVariantsRequest
        {
            Filter = "(displayName=*shirt/i|sku=*shirt/i|barcode=*shirt/i)",
            OrderBy = "updatedOnUtc desc"
        };

        var result = new GetProductVariantsRequestValidator().Validate(request);

        result.IsValid.ShouldBeTrue();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ShopifyProductVariantEntity CreateVariant(
        string displayName,
        string sku,
        string barcode,
        int failedSyncAttempts) => new()
    {
        GlobalProductId = $"gid://shopify/Product/{Guid.NewGuid()}",
        ProductId = Random.Shared.NextInt64(1, long.MaxValue),
        GlobalVariantId = $"gid://shopify/ProductVariant/{Guid.NewGuid()}",
        VariantId = Random.Shared.NextInt64(1, long.MaxValue),
        DisplayName = displayName,
        Sku = sku,
        Barcode = barcode,
        FailedShopifySyncAttempts = failedSyncAttempts,
        PendingShopifySync = failedSyncAttempts > 0,
        IsActive = true
    };
}
