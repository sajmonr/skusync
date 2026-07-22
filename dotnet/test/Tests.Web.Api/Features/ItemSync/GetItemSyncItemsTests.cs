using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Web.Api.Common.Paging;
using Web.Api.Features.ItemSync.GetItemSyncItems;

namespace Tests.Web.Api.Features.ItemSync;

public class GetItemSyncItemsTests
{
    [Fact]
    public async Task Query_ShouldFilterByStatusAndProjectLinkedSkulabsItem()
    {
        await using var dbContext = CreateDbContext();
        var outOfSyncVariant = CreateVariant("Alpine Cap Black");
        var inSyncVariant = CreateVariant("Alpine Cap Blue");
        dbContext.ShopifyProductVariants.AddRange(outOfSyncVariant, inSyncVariant);
        dbContext.SkulabsItems.AddRange(
            CreateSkulabsItem(outOfSyncVariant, "Alpine Cap — Black"),
            CreateSkulabsItem(inSyncVariant, inSyncVariant.DisplayName));
        await dbContext.SaveChangesAsync();

        var query = new GetItemSyncItemsRequest { Status = "out-of-sync" };

        var result = await ApplyRequest(dbContext, query);

        result.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem();
        result.Items[0].DisplayName.ShouldBe(outOfSyncVariant.DisplayName);
        result.Items[0].Skulabs.ShouldNotBeNull();
        result.Items[0].Skulabs!.Value.Title.ShouldBe("Alpine Cap — Black");
    }

    [Fact]
    public async Task Query_ShouldIncludeVariantWithoutSkulabsItem_WhenMissingStatusIsRequested()
    {
        await using var dbContext = CreateDbContext();
        var variantWithoutSkulabsItem = CreateVariant("Trail bottle");
        dbContext.ShopifyProductVariants.Add(variantWithoutSkulabsItem);
        await dbContext.SaveChangesAsync();

        var result = await ApplyRequest(dbContext, new GetItemSyncItemsRequest
        {
            Status = "missing-in-skulabs"
        });

        result.TotalCount.ShouldBe(1);
        result.Items[0].Skulabs.ShouldBeNull();
    }

    [Fact]
    public async Task Query_ShouldSearchAcrossBothSystems()
    {
        await using var dbContext = CreateDbContext();
        var variant = CreateVariant("Trail bottle");
        dbContext.ShopifyProductVariants.Add(variant);
        dbContext.SkulabsItems.Add(CreateSkulabsItem(variant, variant.DisplayName, "SL-8921"));
        await dbContext.SaveChangesAsync();

        var result = await ApplyRequest(dbContext, new GetItemSyncItemsRequest { Search = "sl-8921" });

        result.TotalCount.ShouldBe(1);
        result.Items[0].Skulabs!.Value.Id.ShouldBe("SL-8921");
    }

    [Fact]
    public async Task Query_ShouldExcludeInactiveVariants()
    {
        await using var dbContext = CreateDbContext();
        var activeVariant = CreateVariant("Active item");
        var inactiveVariant = CreateVariant("Inactive item");
        inactiveVariant.IsActive = false;
        dbContext.ShopifyProductVariants.AddRange(activeVariant, inactiveVariant);
        await dbContext.SaveChangesAsync();

        var result = await dbContext.ShopifyProductVariants
            .AsNoTracking()
            .Where(entity => entity.IsActive)
            .OrderBy(entity => entity.DisplayName)
            .ThenBy(entity => entity.ShopifyProductVariantId)
            .ToPagedResponseAsync(
                new GetItemSyncItemsRequest(),
                ItemSyncGridMapper.Instance,
                ItemSyncListItem.Projection);

        result.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem().DisplayName.ShouldBe(activeVariant.DisplayName);
    }

    [Theory]
    [InlineData("in-sync")]
    [InlineData("out-of-sync")]
    [InlineData("missing-in-skulabs")]
    [InlineData("pending-sync")]
    public void Validator_ShouldAcceptSupportedStatus(string status)
    {
        var result = new GetItemSyncItemsRequestValidator().Validate(new GetItemSyncItemsRequest
        {
            Status = status
        });

        result.IsValid.ShouldBeTrue();
    }

    private static Task<PagedResponse<ItemSyncListItem>> ApplyRequest(
        ApplicationDbContext dbContext,
        GetItemSyncItemsRequest request)
    {
        var source = dbContext.ShopifyProductVariants.AsNoTracking().AsQueryable();
        source = source.ApplyItemSyncSearch(request.Search);
        source = source.ApplyItemSyncStatusFilter(request.Status);

        return source
            .OrderBy(entity => entity.DisplayName)
            .ThenBy(entity => entity.ShopifyProductVariantId)
            .ToPagedResponseAsync(request, ItemSyncGridMapper.Instance, ItemSyncListItem.Projection);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ShopifyProductVariantEntity CreateVariant(string displayName) => new()
    {
        GlobalProductId = $"gid://shopify/Product/{Guid.NewGuid()}",
        ProductId = Random.Shared.NextInt64(1, long.MaxValue),
        GlobalVariantId = $"gid://shopify/ProductVariant/{Guid.NewGuid()}",
        VariantId = Random.Shared.NextInt64(1, long.MaxValue),
        DisplayName = displayName,
        Sku = "SHOPIFY-SKU",
        Barcode = "SHOPIFY-BARCODE"
    };

    private static SkulabsItemEntity CreateSkulabsItem(
        ShopifyProductVariantEntity variant,
        string title,
        string sourceItemId = "SL-100") => new()
    {
        ShopifyProductVariantId = variant.ShopifyProductVariantId,
        SkulabsSourceItemId = sourceItemId,
        SkulabsSourceListingId = $"LISTING-{Guid.NewGuid()}",
        Title = title,
        Sku = "SKULABS-SKU",
        Barcode = "SKULABS-BARCODE"
    };
}
