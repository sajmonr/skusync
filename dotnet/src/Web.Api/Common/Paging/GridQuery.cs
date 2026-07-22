using Gridify;

namespace Web.Api.Common.Paging;

public class GridQuery : GridifyQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 250;

    public GridQuery()
    {
        Page = DefaultPage;
        PageSize = DefaultPageSize;
    }
}
