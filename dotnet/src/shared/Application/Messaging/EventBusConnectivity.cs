using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.Messaging;

/// <summary>
/// Startup connectivity preflight for the RabbitMQ event bus. Mirrors the database migration step
/// in that it runs after <c>builder.Build()</c> and before the host starts serving/consuming, so a
/// host that cannot reach the broker crashes loudly at startup instead of running dead — the silent
/// outage this health work exists to prevent (a swallowed <c>AutoStart</c> error left consumers
/// registered on a null channel).
/// </summary>
public static class EventBusConnectivity
{
    extension(IHost host)
    {
        /// <summary>
        /// Opens the RabbitMQ connection (the same one the readiness health check reuses) to confirm
        /// the broker is reachable with the configured credentials. Logs fatal and rethrows on
        /// failure so the process exits and the orchestrator restart-loops with a clear reason. Call
        /// this only from hosts that consume events (AppServer); producing hosts (Web.Api) surface
        /// broker trouble through the readiness health check instead of crashing.
        /// </summary>
        public async Task VerifyEventBusConnectivity(CancellationToken cancellationToken = default)
        {
            var logger = host.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(EventBusConnectivity).FullName!);

            try
            {
                await host.Services
                    .GetRequiredService<RabbitMqConnectionProvider>()
                    .GetConnection(cancellationToken);

                logger.LogInformation("RabbitMQ connectivity verified at startup.");
            }
            catch (Exception exception)
            {
                logger.LogCritical(
                    exception,
                    "Could not connect to RabbitMQ using the '{ConnectionStringName}' connection "
                        + "string. The event bus is unreachable — aborting startup so the container "
                        + "restarts instead of running with dead consumers.",
                    ApplicationEventBus.ConnectionStringName);
                throw;
            }
        }
    }
}
