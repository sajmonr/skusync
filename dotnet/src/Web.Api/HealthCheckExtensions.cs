using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Web.Api;

/// <summary>
/// Extension methods that wire up the application's health-check endpoints. Lives outside
/// <see cref="DependencyInjection"/> because endpoint mapping happens on
/// <see cref="WebApplication"/>, not on <c>IHostApplicationBuilder</c>.
/// </summary>
public static class HealthCheckExtensions
{
    extension(IHealthChecksBuilder healthChecks)
    {
        /// <summary>
        /// Adds a trivial "self" check that always returns healthy. Tagged <c>live</c> so it's
        /// the only check that runs behind <c>/_health/live</c> — a liveness probe must never
        /// depend on external services, otherwise a downstream outage would trigger a
        /// container-restart loop.
        /// </summary>
        public IHealthChecksBuilder AddSelfCheck() =>
            healthChecks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// Maps three health endpoints, partitioned by tag so each one fails for a
        /// different reason:
        /// <list type="bullet">
        ///   <item><description><c>/_health/live</c> — process is up. Runs only the
        ///     <c>self</c> check. Wire container restart probes here. Must stay narrow:
        ///     failures here trigger restarts.</description></item>
        ///   <item><description><c>/_health/ready</c> — ready to serve traffic. Runs every
        ///     <c>ready</c>-tagged check (Postgres). Wire load-balancer / orchestrator
        ///     readiness probes here.</description></item>
        ///   <item><description><c>/_health</c> — everything. Useful for humans and
        ///     dashboards.</description></item>
        /// </list>
        /// </summary>
        public WebApplication MapHealthCheckEndpoints()
        {
            app.MapHealthChecks("/_health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            app.MapHealthChecks("/_health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            app.MapHealthChecks("/_health", new HealthCheckOptions
            {
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            return app;
        }
    }
}
