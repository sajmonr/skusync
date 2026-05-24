using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace Integration.Skulabs.Items;

/// <summary>
/// Defines the resilience pipeline applied to outbound calls to the SkuLabs API. Lives
/// in its own class so both production DI and integration tests configure exactly the
/// same retry behaviour.
/// </summary>
public static class SkulabsResiliencePipeline
{
    /// <summary>Production default for the initial retry delay (subsequent attempts back off exponentially).</summary>
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Hard cap on any single retry wait — applied AFTER deriving the delay from
    /// <c>Retry-After</c>, so even a hostile or buggy upstream sending
    /// <c>Retry-After: 43200</c> (12 hours) can't lock the sync job slot for that long.
    /// One minute fits comfortably inside our 10-minute job cadence: if SkuLabs is still
    /// angry after a minute of retries we abandon the batch and let the next tick try.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Configures retry-only resilience with the production default delays: 3 attempts,
    /// exponential backoff with jitter, 2s base, capped at 60s per wait. Retries on
    /// network failures, timeouts, 429 Too Many Requests, 408 Request Timeout and any 5xx.
    /// Honours the <c>Retry-After</c> header on 429 responses up to <see cref="DefaultMaxDelay"/>.
    /// </summary>
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline) =>
        Configure(pipeline, DefaultBaseDelay, DefaultMaxDelay);

    /// <summary>
    /// Same as <see cref="Configure(ResiliencePipelineBuilder{HttpResponseMessage})"/> but with
    /// caller-supplied base and max delays. Intended for tests so they don't pay the full
    /// backoff cost and can assert that <paramref name="maxDelay"/> caps oversized
    /// <c>Retry-After</c> values.
    /// </summary>
    public static void Configure(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        TimeSpan baseDelay,
        TimeSpan maxDelay)
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = baseDelay,
            MaxDelay = maxDelay,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TimeoutException>()
                .HandleResult(response =>
                    response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.RequestTimeout ||
                    (int)response.StatusCode >= 500),
            // Polly's MaxDelay caps the *computed* exponential backoff, but the built-in
            // Retry-After honouring in HttpRetryStrategyOptions returns the header value
            // through a DelayGenerator — which bypasses MaxDelay. A hostile or buggy
            // upstream could otherwise send Retry-After: 43200 and lock the sync job slot
            // for 12 hours. Clamp it ourselves.
            DelayGenerator = args =>
            {
                if (args.Outcome.Result is { } response &&
                    response.Headers.RetryAfter is { } retryAfter)
                {
                    var headerDelay = ResolveRetryAfter(retryAfter);
                    if (headerDelay is { } delay)
                    {
                        return ValueTask.FromResult<TimeSpan?>(delay > maxDelay ? maxDelay : delay);
                    }
                }

                // Fall back to the default exponential-backoff calculation (which already
                // respects MaxDelay because Polly applies the cap on its own backoff path).
                return ValueTask.FromResult<TimeSpan?>(null);
            }
        });
    }

    /// <summary>
    /// Translates a <see cref="System.Net.Http.Headers.RetryConditionHeaderValue"/> into a
    /// concrete wait. The header can be either a delta (delta-seconds, the common case for
    /// rate limits) or an HTTP-date.
    /// </summary>
    private static TimeSpan? ResolveRetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue retryAfter)
    {
        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }
        if (retryAfter.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        return null;
    }
}
