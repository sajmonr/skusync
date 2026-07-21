using Application;
using Infrastructure;
using Integration;
using Microsoft.Extensions.Hosting;

namespace AppServer;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers the full AppServer processing composition: outbound integrations, the SQS
        /// webhook consumer, infrastructure, application services, Shopify webhook handlers,
        /// in-memory event consumers, and scheduled Quartz jobs. Shared by the AppServer host
        /// and its end-to-end test host so both compose identically.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddAppServer()
        {
            builder.AddIntegration()
                .AddSqsWebhookConsumer()
                .AddInfrastructure()
                .AddApplication()
                .AddShopifyWebhookHandlers()
                .AddInMemoryEventProcessing()
                .AddScheduledJobs();

            return builder;
        }
    }
}
