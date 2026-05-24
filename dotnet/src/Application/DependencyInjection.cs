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
        /// Registers all Application-layer services, webhook handlers, and the Quartz.NET
        /// scheduled job infrastructure with the dependency injection container.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddApplication()
        {
            builder.Services.AddFeatureManagement();
            builder.AddOptionsFromConfiguration<SkuGeneratorOptions>(SkuGeneratorOptions.SectionKey);

            return builder
                .AddApplicationServicesServices()
                .AddShopifyWebhooks()
                .AddMessageBus()
                .AddScheduledJobs();
        }

        private T AddShopifyWebhooks()
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

        private T AddApplicationServicesServices()
        {
            builder.Services.AddTransient<IProductsService, ProductsService>();
            builder.Services.AddTransient<ISkulabsItemSyncService, SkulabsItemSyncService>();
            builder.Services.AddTransient<ISkuGenerator, SkuGenerator>();
            builder.Services.AddTransient<ISkuAndBarcodeSyncService, SkuAndBarcodeSyncService>();
            builder.Services.AddTransient<ISkulabsTitleSyncService, SkulabsTitleSyncService>();

            builder.Services.AddTransient<IMaintenanceTask, ShopifyProductSyncTask>();
            builder.Services.AddTransient<IMaintenanceTask, SkuAndBarcodeSyncTask>();
            builder.Services.AddTransient<IMaintenanceTask, SkulabsTitleSyncTask>();

            return builder;
        }

        private T AddMessageBus()
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

        private T AddScheduledJobs()
        {
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
