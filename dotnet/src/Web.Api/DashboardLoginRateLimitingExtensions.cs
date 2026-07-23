using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Web.Api;

public static class DashboardLoginRateLimitingExtensions
{
    public const string PolicyName = "dashboard-login";

    extension(IServiceCollection services)
    {
        public IServiceCollection AddDashboardLoginRateLimiting() => services.AddRateLimiter(options =>
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })));
    }
}
