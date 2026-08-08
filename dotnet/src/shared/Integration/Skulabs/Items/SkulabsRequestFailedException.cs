using System.Net;

namespace Integration.Skulabs.Items;

/// <summary>
/// A SkuLabs request that came back as a failure, carrying enough of the error envelope for the
/// caller to decide whether retrying could ever help.
/// <para>
/// Rate limiting is deliberately <em>not</em> represented here — it surfaces as
/// <see cref="RateLimiting.RateLimitedException"/> so callers cannot accidentally treat "come back
/// later" as "this request is broken".
/// </para>
/// </summary>
public sealed class SkulabsRequestFailedException : Exception
{
    public SkulabsRequestFailedException(
        string requestPath,
        HttpStatusCode statusCode,
        string? message,
        bool userError,
        string? skulabsTraceId)
        : base($"SkuLabs request to '{requestPath}' failed with {(int)statusCode}. {message}".TrimEnd())
    {
        RequestPath = requestPath;
        StatusCode = statusCode;
        UserError = userError;
        SkulabsTraceId = skulabsTraceId;
    }

    public string RequestPath { get; }

    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// SkuLabs' own <c>user_error</c> flag — "caused by input" rather than by their systems. Useful
    /// context, but not on its own a reason to give up on an item: a 429 also sets it.
    /// </summary>
    public bool UserError { get; }

    /// <summary>The trace id to quote when asking SkuLabs support about this failure.</summary>
    public string? SkulabsTraceId { get; }

    /// <summary>
    /// Whether the failure is about <em>our credentials</em> rather than the payload we sent.
    /// <para>
    /// This distinction decides who gets punished. An expired token or a revoked scope fails every
    /// request identically, so counting it against individual items would march the whole catalogue
    /// to the exclusion threshold within a few dispatch cycles and leave an operator to unpick it by
    /// hand — for a problem that a single credential fix resolves. Payload-level failures (400) stay
    /// attributable to the batch and keep their strikes.
    /// </para>
    /// </summary>
    public bool IsCredentialFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
