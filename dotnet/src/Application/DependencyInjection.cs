using Application.Events;
using Application.Jobs;
using Application.Queue.ShopifyProductUpdate;
using Application.Shopify;
using Integration.Aws.Sqs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using SharedKernel.Options;

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
            // Singleton accumulator shared by all producers (import service + webhook handlers).
            builder.Services.AddSingleton<IProductEventAccumulator, ProductEventAccumulator>();

            builder.Services.AddTransient<IShopifyService, ShopifyService>();

            builder.Services.AddTransient<IShopifyWebhookHandler, ShopifyProductUpdateWebhookHandler>();
            builder.Services.AddTransient<IShopifyWebhookHandler, ShopifyProductCreateWebhookHandler>();

            builder.AddOptionsFromConfiguration<ScheduledJobsOptions>(ScheduledJobsOptions.SectionKey);
            var scheduledJobsOptions = builder.GetRequiredConfigValue<ScheduledJobsOptions>(ScheduledJobsOptions.SectionKey);

            builder.Services.AddQuartz(quartz =>
            {
                quartz.AddScheduledJob<ShopifySyncJob>(ShopifySyncJob.Key, scheduledJobsOptions.ShopifyProductSync);
                quartz.AddScheduledJob<ProductEventProcessorJob>(ProductEventProcessorJob.Key, scheduledJobsOptions.ProductEventProcessor);
            });

            builder.Services.AddQuartzHostedService(options =>
            {
                options.WaitForJobsToComplete = true;
            });

            return builder;
        }
    }
}
