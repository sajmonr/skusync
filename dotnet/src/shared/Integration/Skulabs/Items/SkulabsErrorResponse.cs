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
    [property: JsonPropertyName("user_error")] bool? UserError,
    [property: JsonPropertyName("data")] SkulabsErrorData? Data = null);

/// <summary>
/// The <c>data</c> object SkuLabs attaches to an error. Only the rate-limit fields are mapped —
/// they are the ones we act on, because <b>SkuLabs sends no <c>Retry-After</c> header</b> and the
/// wait is available nowhere else.
/// </summary>
/// <param name="WaitSeconds">
/// How long until the quota admits another request. Measured at ~1508s against a 3600s window, so
/// it is far larger than any header-derived retry delay elsewhere in this client.
/// </param>
/// <param name="Limit">Requests permitted per <paramref name="IntervalSeconds"/>, per account.</param>
/// <param name="IntervalSeconds">Width of the quota window.</param>
public sealed record SkulabsErrorData(
    [property: JsonPropertyName("wait_seconds")] double? WaitSeconds,
    [property: JsonPropertyName("limit")] int? Limit,
    [property: JsonPropertyName("interval_seconds")] int? IntervalSeconds);
