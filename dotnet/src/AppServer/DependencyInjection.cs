using Application;
using Hosting;
using Infrastructure;
using Integration;
using Microsoft.Extensions.DependencyInjection;

namespace AppServer;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers the full AppServer processing composition: outbound integrations, the SQS
        /// webhook consumer, infrastructure, application services, webhook processing, and the
        /// Hangfire background-job server with its scheduled recurring jobs. Shared by the
        /// AppServer host and its end-to-end test host so both compose identically.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddAppServer()
        {
            builder
                .AddSqsWebhookConsumer()
                .AddInfrastructure()
                .AddApplication()
                .AddWebhookProcessing()
                .AddHangfireProcessing();

            // Liveness self-check. The Postgres ('ready') check is registered transitively by
            // AddInfrastructure; the endpoints that expose them are mapped in Program.cs via
            // MapHealthCheckEndpoints.
            builder.Services.AddHealthChecks().AddSelfCheck();

            return builder;
        }
    }
}
