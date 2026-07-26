using FastEndpoints;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Web.Api.Common.Paging;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public class GetAmbiguousItemsEndpoint(ApplicationDbContext dbContext)
    : Endpoint<GetAmbiguousItemsRequest, PagedResponse<AmbiguousItemListItem>>
{
    public override void Configure()
    {
        Get("ambiguous-items");
        Summary(summary =>
        {
            summary.Summary = "List ambiguous SkuLabs items";
            summary.Description =
                "Returns paged SkuLabs items that could not be cleanly mapped to a single Shopify "
                + "variant (multiple listings, none, or a non-Shopify listing), each with its listings.";
        });
    }

    public override async Task HandleAsync(
        GetAmbiguousItemsRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SkulabsAmbiguousItems.AsNoTracking();

        query = query.ApplyAmbiguousItemsSearch(request.Search);
        query = query.ApplyAmbiguousItemsReasonFilter(request.Reason);

        var pagedResponse = await query
            .OrderByDescending(entity => entity.FirstSeenUtc)
            .ThenBy(entity => entity.SkulabsAmbiguousItemId)
            .ToPagedResponseAsync(
                request,
                AmbiguousItemsGridMapper.Instance,
                AmbiguousItemListItem.Projection,
                cancellationToken);

        var response = new PagedResponse<AmbiguousItemListItem>(
            pagedResponse.Items.Select(item => item.WithExternalUrls()).ToArray(),
            pagedResponse.TotalCount,
            pagedResponse.Page,
            pagedResponse.PageSize);

        await Send.OkAsync(response, cancellationToken);
    }
}
