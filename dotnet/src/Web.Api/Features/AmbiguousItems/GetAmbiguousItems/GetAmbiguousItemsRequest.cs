using Web.Api.Common.Paging;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public class GetAmbiguousItemsRequest : GridQuery
{
    public string? Search { get; set; }

    /// <summary>Optional exact reason name to filter by (e.g. <c>MultipleListings</c>).</summary>
    public string? Reason { get; set; }
}
