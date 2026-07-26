using FluentValidation;
using SharedKernel;
using Web.Api.Common.Paging;

namespace Web.Api.Features.AmbiguousItems.GetAmbiguousItems;

public class GetAmbiguousItemsRequestValidator : GridQueryValidator<GetAmbiguousItemsRequest>
{
    private static readonly string[] ValidReasons =
        Enum.GetNames<SkulabsAmbiguityReason>();

    public GetAmbiguousItemsRequestValidator()
    {
        RuleFor(request => request.Search)
            .MaximumLength(200);

        RuleFor(request => request.Reason)
            .Must(reason => reason is null || ValidReasons.Contains(reason))
            .WithMessage($"Reason must be one of: {string.Join(", ", ValidReasons)}.");

        AddGridifyValidation(AmbiguousItemsGridMapper.Instance);
    }
}
