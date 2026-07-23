using System.Linq.Expressions;
using Gridify;
using Microsoft.EntityFrameworkCore;

namespace Web.Api.Common.Paging;

public static class QueryablePagingExtensions
{
    public static async Task<PagedResponse<TResponse>> ToPagedResponseAsync<TEntity, TResponse>(
        this IQueryable<TEntity> source,
        GridQuery query,
        IGridifyMapper<TEntity> mapper,
        Expression<Func<TEntity, TResponse>> projection,
        CancellationToken cancellationToken = default)
    {
        var filteredQuery = source.ApplyFiltering(query, mapper);
        var totalCount = await filteredQuery.CountAsync(cancellationToken);

        var items = await filteredQuery
            .ApplyOrderingAndPaging(query, mapper)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return new PagedResponse<TResponse>(items, totalCount, query.Page, query.PageSize);
    }
}
