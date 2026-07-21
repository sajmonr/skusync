using System.Reflection;
using Application.Jobs;
using Application.Jobs.Maintenance;
using Application.Products.Maintenance;
using Application.Products.Services;
using Application.Products.Webhook;
using Application.Skulabs.Jobs;
using Application.Skulabs.Maintenance;
using Application.Skulabs.Services;
using Application.Skus;
using Integration.Aws.Sqs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using Quartz;
using SharedKernel.Options;
using SlimMessageBus.Host;
using SlimMessageBus.Host.Memory;

namespace Application;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers Application-layer services and their supporting configuration with the
        /// dependency injection container. This method does not start hosted processing.
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

            return builder;
        }

        /// <summary>
        /// Registers the message bus and discovers Application-layer event consumers.
        /// Only hosts responsible for processing application events should call this method.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddEventProcessing()
        {
            builder.Services.AddSlimMessageBus(busBuilder =>
            {
                busBuilder
                    .WithProviderMemory(config =>
                    {
                        config.EnableBlockingPublish = false;
                    })
                    .AutoDeclareFrom(Assembly.GetExecutingAssembly());
            });

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
