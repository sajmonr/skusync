using Web.Api.Common.Paging;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public class GetAmbiguousItemsRequest : GridQuery
{
    public string? Search { get; set; }
}
