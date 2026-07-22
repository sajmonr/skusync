using FastEndpoints;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Web.Api.Common.Paging;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public class GetItemSyncItemsEndpoint(ApplicationDbContext dbContext)
    : Endpoint<GetItemSyncItemsRequest, PagedResponse<ItemSyncListItem>>
{
    public override void Configure()
    {
        Get("item-sync");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "List item synchronization records";
            summary.Description = "Returns paged Shopify variants with their linked SkuLabs item, when present.";
        });
    }

    public override async Task HandleAsync(
        GetItemSyncItemsRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ShopifyProductVariants
            .AsNoTracking()
            .Where(entity => entity.IsActive)
            .AsQueryable();

        query = query.ApplyItemSyncSearch(request.Search);
        query = query.ApplyItemSyncStatusFilter(request.Status);

        var response = await query
            .OrderBy(entity => entity.DisplayName)
            .ThenBy(entity => entity.ShopifyProductVariantId)
            .ToPagedResponseAsync(
                request,
                ItemSyncGridMapper.Instance,
                ItemSyncListItem.Projection,
                cancellationToken);

        await Send.OkAsync(response, cancellationToken);
    }
}
