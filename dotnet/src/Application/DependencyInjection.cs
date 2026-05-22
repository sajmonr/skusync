using System.Reflection;
using Application.Jobs;
using Application.Products.Jobs;
using Application.Products.Services;
using Application.Products.Webhook;
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

            return builder.AddApplicationServicesServices()
                .AddShopifyWebhooks()
                .AddMessageBus()
                .AddScheduledJobs();
        }

        private T AddShopifyWebhooks()
        {
            builder.Services.AddTransient<IShopifyWebhookHandler, ShopifyProductUpdateWebhookHandler>();
            builder.Services.AddTransient<IShopifyWebhookHandler, ShopifyProductCreateWebhookHandler>();

            return builder;
        }

        private T AddApplicationServicesServices()
        {
            builder.Services.AddTransient<IProductsService, ProductsService>();

            return builder;
        }

        private T AddMessageBus()
        {
            builder.Services.AddSlimMessageBus(busBuilder =>
            {
                busBuilder.WithProviderMemory(config => { config.EnableBlockingPublish = false; })
                    .AutoDeclareFrom(Assembly.GetExecutingAssembly());
            });

            return builder;
        }

        private T AddScheduledJobs()
        {
            builder.AddOptionsFromConfiguration<ScheduledJobsOptions>(ScheduledJobsOptions.SectionKey);
            var scheduledJobsOptions =
                builder.GetRequiredConfigValue<ScheduledJobsOptions>(ScheduledJobsOptions.SectionKey);

            builder.Services.AddSingleton<MutexGroupRegistry>();
            builder.Services.AddSingleton<MutexGroupListener>();

            builder.Services.AddQuartz(quartz =>
            {
                //quartz.AddTriggerListener<MutexGroupListener>(GroupMatcher<TriggerKey>.AnyGroup());
                //quartz.AddJobListener<MutexGroupListener>(GroupMatcher<JobKey>.AnyGroup());

                quartz.AddScheduledJob<ShopifyProductSyncJob>(ShopifyProductSyncJob.Key,
                    scheduledJobsOptions.ShopifyProductSync);
            });

            builder.Services.AddQuartzHostedService(options => { options.WaitForJobsToComplete = true; });

            return builder;
        }
    }
}