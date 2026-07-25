using Application.Jobs;
using Application.Jobs.Maintenance;
using Application.Messaging;
using Application.Products.Maintenance;
using Application.Products.Services;
using Application.Products.Webhook;
using Application.Skulabs.Jobs;
using Application.Skulabs.Maintenance;
using Application.Skulabs.Services;
using Application.Skus;
using Integration.Aws.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using Quartz;
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
                ApplicationEventBus.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"A '{ApplicationEventBus.ConnectionStringName}' connection string is required to publish " +
                    "application events. Set ConnectionStrings:RabbitMq (e.g. via an environment variable).");
            }

            builder.Services.AddSlimMessageBus(
                busBuilder => ApplicationEventBus.ConfigureProducers(busBuilder, connectionString));

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

        /// <summary>
        /// Registers maintenance tasks, Quartz jobs, and the hosted Quartz scheduler. Only hosts
        /// responsible for scheduled background processing should call this method.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddScheduledJobs()
        {
            builder.Services.AddTransient<IMaintenanceTask, ShopifyProductSyncTask>();
            builder.Services.AddTransient<IMaintenanceTask, SkuAndBarcodeSyncTask>();
            builder.Services.AddTransient<IMaintenanceTask, SkulabsTitleSyncTask>();

            builder.AddOptionsFromConfiguration<ScheduledJobsOptions>(
                ScheduledJobsOptions.SectionKey
            );
            var scheduledJobsOptions = builder.GetRequiredConfigValue<ScheduledJobsOptions>(
                ScheduledJobsOptions.SectionKey
            );

            builder.Services.AddQuartz(quartz =>
            {
                quartz.AddScheduledJob<SkulabsItemSyncJob>(
                    SkulabsItemSyncJob.Key,
                    scheduledJobsOptions.SkulabsItemSync
                );

                quartz.AddScheduledJob<ProductMaintenanceJob>(
                    ProductMaintenanceJob.Key,
                    scheduledJobsOptions.ProductMaintenance
                );
            });

            builder.Services.AddQuartzHostedService(options =>
            {
                options.WaitForJobsToComplete = true;
            });

            return builder;
        }
    }
}
