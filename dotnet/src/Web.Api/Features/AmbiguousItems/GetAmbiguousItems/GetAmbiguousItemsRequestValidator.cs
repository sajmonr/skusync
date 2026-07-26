using FluentValidation;
using Web.Api.Common.Paging;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public class GetAmbiguousItemsRequestValidator : GridQueryValidator<GetAmbiguousItemsRequest>
{
    public GetAmbiguousItemsRequestValidator()
    {
        RuleFor(request => request.Search)
            .MaximumLength(200);

        AddGridifyValidation(AmbiguousItemsGridMapper.Instance);
    }
}
