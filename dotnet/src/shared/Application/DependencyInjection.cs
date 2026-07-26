using Application.Jobs;
using Application.Messaging;
using Hangfire;
using Application.Products.Services;
using Application.Products.Webhook;
using Application.Skulabs.Services;
using Application.Skus;
using Integration;
using Integration.Aws.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using SharedKernel.Options;
using SlimMessageBus.Host;

namespace Application;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers Application-layer services, their supporting configuration, and the ability to
        /// <em>produce</em> application events onto the RabbitMQ bus. Every host that needs the
        /// Application layer can therefore publish events; consuming them is a separate concern
        /// (see <see cref="AddEventProcessing"/>). Requires the <c>ConnectionStrings:RabbitMq</c>
        /// AMQP connection string, since producing needs a broker connection.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddApplication()
        {
            builder.AddIntegration();
            builder.Services.AddFeatureManagement();
            builder.AddOptionsFromConfiguration<SkuGeneratorOptions>(
                SkuGeneratorOptions.SectionKey
            );

            builder.Services.AddTransient<IProductsService, ProductsService>();
            builder.Services.AddTransient<ISkulabsItemSyncService, SkulabsItemSyncService>();
            builder.Services.AddTransient<ISkuGenerator, SkuGenerator>();
            builder.Services.AddTransient<ISkuAndBarcodeSyncService, SkuAndBarcodeSyncService>();
            builder.Services.AddTransient<ISkulabsTitleSyncService, SkulabsTitleSyncService>();

            var connectionString = builder.Configuration.GetConnectionString(
                ApplicationEventBus.ConnectionStringName
            );
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"A '{ApplicationEventBus.ConnectionStringName}' connection string is required to publish "
                        + "application events. Set ConnectionStrings:RabbitMq (e.g. via an environment variable)."
                );
            }

            builder.Services.AddSlimMessageBus(busBuilder =>
                ApplicationEventBus.ConfigureProducers(busBuilder, connectionString)
            );

            // Hangfire client: every host that has the Application layer can enqueue background
            // jobs (Web.Api enqueues; AppServer both enqueues and processes). Processing is a
            // separate opt-in — see AddHangfireProcessing.
            var hangfireConnection = builder.Configuration.GetConnectionString(
                HangfireConfiguration.ConnectionStringName
            );
            if (string.IsNullOrWhiteSpace(hangfireConnection))
            {
                throw new InvalidOperationException(
                    $"A '{HangfireConfiguration.ConnectionStringName}' connection string is required for "
                        + "Hangfire background-job storage."
                );
            }

            builder.Services.AddHangfire(config =>
                HangfireConfiguration.Configure(config, hangfireConnection)
            );

            // A dedicated RabbitMQ connection for the health check + startup preflight, kept
            // separate from the message bus's own connection (SlimMessageBus doesn't expose it).
            builder.Services.AddSingleton(new RabbitMqConnectionProvider(connectionString));

            // Async factory overload so the connection is opened without blocking; the provider
            // opens it once and reuses it. Tagged 'ready' (never 'live'): a broker outage must fail
            // readiness, not liveness — liveness driving a restart loop on an external dependency is
            // exactly what we avoid.
            builder.Services.AddHealthChecks()
                .AddRabbitMQ(
                    sp => sp.GetRequiredService<RabbitMqConnectionProvider>().GetConnection(),
                    name: "rabbitmq",
                    tags: ["ready", "rabbitmq"]);

            return builder;
        }

        /// <summary>
        /// Registers the Hangfire background-job <em>server</em> that dequeues and executes jobs from
        /// shared storage. Only hosts responsible for processing jobs (currently AppServer) should
        /// call this, and only after <see cref="AddApplication"/>, which establishes the Hangfire
        /// client and storage.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddHangfireProcessing()
        {
            builder.Services.AddHangfireServer();

            builder.Services.AddTransient<RecurringJobs>();
            builder.Services.AddTransient<FullSyncOrchestrator>();
            builder.AddOptionsFromConfiguration<ScheduledJobsOptions>(
                ScheduledJobsOptions.SectionKey
            );
            builder.Services.AddHostedService<RecurringJobRegistrar>();

            return builder;
        }

        /// <summary>
        /// Registers the handlers for Shopify webhook topics. Only hosts responsible for
        /// webhook processing should call this method.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddWebhookProcessing()
        {
            builder.Services.AddTransient<
                IShopifyWebhookHandler,
                ShopifyProductUpdateWebhookHandler
            >();
            builder.Services.AddTransient<
                IShopifyWebhookHandler,
                ShopifyProductCreateWebhookHandler
            >();
            builder.Services.AddTransient<
                IShopifyWebhookHandler,
                ShopifyProductDeleteWebhookHandler
            >();

            return builder;
        }

        /// <summary>
        /// Registers the Application-layer event consumers and binds their RabbitMQ work queues to
        /// the matching exchanges. Only hosts responsible for processing application events
        /// (currently AppServer) should call this method, and only after <see cref="AddApplication"/>,
        /// which establishes the bus provider and serializer.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddEventProcessing()
        {
            builder.Services.AddSlimMessageBus(ApplicationEventBus.ConfigureConsumers);

            return builder;
        }
    }
}
