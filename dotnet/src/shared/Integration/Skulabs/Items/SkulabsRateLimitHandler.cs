using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Integration.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Integration.Skulabs.Items;

/// <summary>
/// Outer-most handler on the SkuLabs <see cref="HttpClient"/> pipeline. Records a cooldown
/// in <see cref="IRateLimitService"/> whenever SkuLabs responds with 429 — the FINAL
/// outcome of the request, since retries inside the resilience pipeline run below this
/// handler — so subsequent calls can short-circuit before reaching the network. The
/// pre-check itself lives in <see cref="SkulabsItemClient"/> so requests in cooldown never
/// even build an <see cref="HttpRequestMessage"/>.
/// <para>
/// <b>SkuLabs sends no <c>Retry-After</c> header on 429</b> — the wait is carried in the body at
/// <c>error.data.wait_seconds</c> and nowhere else. The header is still preferred when present so
/// a fronting proxy or a future API revision keeps working, but the body is what actually answers
/// in production. Getting this wrong is expensive rather than merely untidy: the fallback cooldown
/// is minutes while a real wait is tens of minutes, so every rate limit would otherwise resume too
/// early, earn another 429, and spend quota discovering the same limit repeatedly.
/// </para>
/// </summary>
public class SkulabsRateLimitHandler : DelegatingHandler
{
    /// <summary>
    /// Ceiling applied to a body-derived wait. SkuLabs quotas are windowed (measured at 3600s), so
    /// no legitimate wait can exceed one window — anything longer is a malformed or hostile payload
    /// that would otherwise mute the client for hours.
    /// </summary>
    private static readonly TimeSpan MaxBodyDerivedCooldown = TimeSpan.FromHours(1);

    /// <summary>
    /// Cap on how much of a 429 body we buffer to look for a wait. Error envelopes are well under
    /// a kilobyte; the cap stops a malformed upstream streaming an unbounded body into memory.
    /// </summary>
    private const int MaxBufferedErrorBodyBytes = 64 * 1024;

    /// <summary>Key used to identify SkuLabs entries in <see cref="IRateLimitService"/>.</summary>
    public const string RateLimitKey = "skulabs";

    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<SkulabsRateLimitHandler> _logger;

    public SkulabsRateLimitHandler(
        IRateLimitService rateLimitService,
        ILogger<SkulabsRateLimitHandler> logger)
    {
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var (retryAfter, source) = await ResolveCooldown(response, cancellationToken);
            _rateLimitService.RecordRateLimit(RateLimitKey, retryAfter);

            var appliedCooldown = retryAfter ?? InMemoryRateLimitService.DefaultCooldown;
            _logger.LogWarning(
                "SkuLabs returned 429 for {RequestUri}. Subsequent calls will short-circuit for {CooldownSeconds}s "
                + "({Source}).",
                request.RequestUri,
                appliedCooldown.TotalSeconds,
                source);
        }

        return response;
    }

    /// <summary>
    /// Resolves how long to stay quiet, preferring the standard header and falling back to the
    /// body — which for this API is the only place the wait actually appears.
    /// </summary>
    private async Task<(TimeSpan? Cooldown, string Source)> ResolveCooldown(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (ResolveRetryAfter(response.Headers.RetryAfter) is { } fromHeader)
        {
            return (fromHeader, "from Retry-After header");
        }

        if (await ResolveWaitFromBody(response, cancellationToken) is { } fromBody)
        {
            return (fromBody, "from error.data.wait_seconds");
        }

        return (null, "default — no Retry-After header and no usable wait_seconds in the body");
    }

    /// <summary>
    /// Reads <c>error.data.wait_seconds</c> out of a 429 body. The content is buffered first so
    /// that callers downstream — notably <see cref="SkulabsItemClient"/>'s error logging — can
    /// still read the same response.
    /// </summary>
    private async Task<TimeSpan?> ResolveWaitFromBody(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await response.Content.LoadIntoBufferAsync(MaxBufferedErrorBodyBytes, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var waitSeconds = JsonSerializer
                .Deserialize<SkulabsErrorResponse>(body)?.Error?.Data?.WaitSeconds;

            if (waitSeconds is not > 0)
            {
                return null;
            }

            var wait = TimeSpan.FromSeconds(waitSeconds.Value);
            return wait > MaxBodyDerivedCooldown ? MaxBodyDerivedCooldown : wait;
        }
        catch (Exception exception)
            when (exception is JsonException or HttpRequestException or IOException
                      or NotSupportedException or ObjectDisposedException)
        {
            // A 429 without a parseable envelope still needs to record a cooldown, so fall back to
            // the default rather than letting a malformed body suppress rate-limit tracking.
            _logger.LogDebug(
                exception,
                "Could not read a wait time from the SkuLabs 429 body; falling back to the default cooldown.");
            return null;
        }
    }

    private static TimeSpan? ResolveRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (retryAfter.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }

        return null;
    }
}
