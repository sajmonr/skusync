using FastEndpoints;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Web.Api.Common.Paging;

namespace Web.Api.Features.ProductVariants.GetProductVariants;

public class GetProductVariantsEndpoint(ApplicationDbContext dbContext)
    : Endpoint<GetProductVariantsRequest, PagedResponse<ProductVariantListItem>>
{
    public override void Configure()
    {
        Get("product-variants");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "List product variants";
            summary.Description = "Returns a filtered, ordered, and paged list of product variants.";
        });
    }

    public override async Task HandleAsync(
        GetProductVariantsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await dbContext.ShopifyProductVariants
            .AsNoTracking()
            .OrderBy(entity => entity.DisplayName)
            .ThenBy(entity => entity.ShopifyProductVariantId)
            .ToPagedResponseAsync(
                request,
                ProductVariantGridMapper.Instance,
                ProductVariantListItem.Projection,
                cancellationToken);

        await Send.OkAsync(response, cancellationToken);
    }
}
