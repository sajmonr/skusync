using FastEndpoints;
using FluentValidation;
using Gridify;

namespace Web.Api.Common.Paging;

public abstract class GridQueryValidator<TRequest> : Validator<TRequest>
    where TRequest : GridQuery
{
    protected GridQueryValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(GridQuery.DefaultPage);

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, GridQuery.MaximumPageSize);
    }

    protected void AddGridifyValidation<TEntity>(IGridifyMapper<TEntity> mapper)
    {
        RuleFor(request => request)
            .Custom((request, context) =>
            {
                if (request.IsValid(out var validationErrors, mapper))
                {
                    return;
                }

                foreach (var validationError in validationErrors)
                {
                    context.AddFailure("query", validationError);
                }
            });
    }
}
