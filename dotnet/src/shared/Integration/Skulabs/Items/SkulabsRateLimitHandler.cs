using System.Net;
using System.Net.Http.Headers;
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
/// </summary>
public class SkulabsRateLimitHandler : DelegatingHandler
{
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
            var retryAfter = ResolveRetryAfter(response.Headers.RetryAfter);
            _rateLimitService.RecordRateLimit(RateLimitKey, retryAfter);

            var appliedCooldown = retryAfter ?? InMemoryRateLimitService.DefaultCooldown;
            _logger.LogWarning(
                "SkuLabs returned 429 for {RequestUri}. Subsequent calls will short-circuit for {CooldownSeconds}s "
                + "({Source}).",
                request.RequestUri,
                appliedCooldown.TotalSeconds,
                retryAfter is null ? "default — no usable Retry-After header" : "from Retry-After header");
        }

        return response;
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
