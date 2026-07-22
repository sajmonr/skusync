using Gridify;
using Shouldly;
using Web.Api.Common.Paging;

namespace Tests.Web.Api.Common.Paging;

public class GridQueryValidatorTests
{
    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, GridQuery.MaximumPageSize + 1)]
    public void Validate_ShouldFailForPagingOutsideSupportedRange(int page, int pageSize)
    {
        var request = new TestGridQuery { Page = page, PageSize = pageSize };

        var result = new TestGridQueryValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_ShouldApplyStandardPagingDefaults()
    {
        var request = new TestGridQuery();

        request.Page.ShouldBe(GridQuery.DefaultPage);
        request.PageSize.ShouldBe(GridQuery.DefaultPageSize);
    }

    [Fact]
    public void Validate_ShouldRejectFieldsThatAreNotExplicitlyMapped()
    {
        var request = new TestGridQuery { Filter = "databaseOnlyName=value" };

        var result = new TestGridQueryValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == "query");
    }

    private sealed class TestGridQuery : GridQuery
    {
    }

    private sealed class TestGridQueryValidator : GridQueryValidator<TestGridQuery>
    {
        public TestGridQueryValidator()
        {
            AddGridifyValidation(new GridifyMapper<TestEntity>().AddMap("publicName", entity => entity.Name));
        }
    }

    private sealed record TestEntity(string Name);
}
