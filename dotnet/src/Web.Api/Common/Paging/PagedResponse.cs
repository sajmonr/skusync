namespace Web.Api.Common.Paging;

public readonly record struct PagedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
