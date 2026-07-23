using Web.Api.Common.Paging;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public class GetItemSyncItemsRequest : GridQuery
{
    public string? Search { get; set; }

    public string? Status { get; set; }
}
