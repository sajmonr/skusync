using System.Text.Json;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Common;

public static class ApiProblemDetails
{
    public static ValidationProblemDetails CreateValidationResponse(
        List<ValidationFailure> failures,
        HttpContext context,
        int statusCode)
    {
        var errors = failures
            .GroupBy(failure => ToJsonPropertyPath(failure.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
            Title = "One or more validation errors occurred.",
            Status = statusCode,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = context.TraceIdentifier;
        return problemDetails;
    }

    private static string ToJsonPropertyPath(string propertyPath) =>
        string.Join(
            '.',
            propertyPath.Split('.').Select(segment =>
            {
                var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
                var propertyName = bracketIndex < 0 ? segment : segment[..bracketIndex];
                var suffix = bracketIndex < 0 ? "" : segment[bracketIndex..];
                return JsonNamingPolicy.CamelCase.ConvertName(propertyName) + suffix;
            }));
}
