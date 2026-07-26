using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Web.Api;

/// <summary>
/// Rate limiting for the manual product-sync trigger: at most one request per 30 seconds per
/// client. A sync is a heavy operation, so this cooldown stops it being hammered even though the
/// endpoint's single-flight guard already prevents overlapping runs.
/// </summary>
public static class ProductSyncRateLimitingExtensions
{
    public const string PolicyName = "product-sync";

    extension(IServiceCollection services)
    {
        public IServiceCollection AddProductSyncRateLimiting() => services.AddRateLimiter(options =>
        {
            // Rejected requests should return 429 Too Many Requests rather than the middleware's
            // default 503. RejectionStatusCode is a global rate-limiter setting, so this applies to
            // every policy (including dashboard-login) — 429 is the correct status for all of them.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromSeconds(30),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
    }
}
