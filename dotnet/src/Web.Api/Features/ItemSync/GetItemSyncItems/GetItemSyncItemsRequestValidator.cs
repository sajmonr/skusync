using Web.Api.Common.Paging;
using FluentValidation;

namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public class GetItemSyncItemsRequestValidator : GridQueryValidator<GetItemSyncItemsRequest>
{
    private static readonly string[] ValidStatuses =
    [
        "in-sync",
        "out-of-sync",
        "missing-in-skulabs",
        "pending-sync"
    ];

    public GetItemSyncItemsRequestValidator()
    {
        RuleFor(request => request.Search)
            .MaximumLength(200);

        RuleFor(request => request.Status)
            .Must(status => status is null || ValidStatuses.Contains(status))
            .WithMessage("Status must be one of: in-sync, out-of-sync, missing-in-skulabs, pending-sync.");

        AddGridifyValidation(ItemSyncGridMapper.Instance);
    }
}
