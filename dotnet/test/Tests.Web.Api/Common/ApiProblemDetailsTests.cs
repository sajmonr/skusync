using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Web.Api.Common;

namespace Tests.Web.Api.Common;

public class ApiProblemDetailsTests
{
    [Fact]
    public void CreateValidationResponse_ShouldGroupErrorsAndUseJsonPropertyNames()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/item-sync";
        context.TraceIdentifier = "trace-id";
        var failures = new List<ValidationFailure>
        {
            new("PageSize", "Page size is too large."),
            new("Filters[0].FieldName", "Field name is invalid."),
            new("PageSize", "Page size is too large.")
        };

        var result = ApiProblemDetails.CreateValidationResponse(failures, context, 400);

        result.Status.ShouldBe(400);
        result.Instance.ShouldBe("/item-sync");
        result.Errors["pageSize"].ShouldBe(["Page size is too large."]);
        result.Errors["filters[0].fieldName"].ShouldBe(["Field name is invalid."]);
        result.Extensions["traceId"].ShouldBe("trace-id");
    }
}
