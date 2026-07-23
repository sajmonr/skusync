using System.Text.Json.Serialization;

namespace Integration.Skulabs.Items;

/// <summary>
/// Standardized error envelope returned by the SkuLabs API on non-2xx responses.
/// The shape mirrors the public SkuLabs error contract; all inner fields are optional
/// because a partial payload is still more useful than nothing when troubleshooting.
/// </summary>
public sealed record SkulabsErrorResponse(
    [property: JsonPropertyName("error")] SkulabsErrorPayload? Error);

public sealed record SkulabsErrorPayload(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("statusCode")] int? StatusCode,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("skulabsTraceId")] string? SkulabsTraceId,
    [property: JsonPropertyName("user_error")] bool? UserError);
