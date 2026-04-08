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
        public T AddApplication()
        {
            builder.Services.AddTransient<IShopifySyncService, ShopifySyncService>();

            builder.Services.AddTransient<IShopifyWebhookHandler, ShopifyProductUpdateWebhookHandler>();
            builder.Services.AddTransient<IShopifyWebhookHandler, ShopifyProductCreateWebhookHandler>();

            builder.AddOptionsFromConfiguration<ScheduledJobsOptions>(ScheduledJobsOptions.SectionKey);
            var scheduledJobsOptions = builder.GetRequiredConfigValue<ScheduledJobsOptions>(ScheduledJobsOptions.SectionKey);

            builder.Services.AddQuartz(quartz =>
            {
                quartz.AddScheduledJob<ShopifySyncJob>(ShopifySyncJob.Key, scheduledJobsOptions.ShopifyProductSync);
            });

            builder.Services.AddQuartzHostedService(options =>
            {
                options.WaitForJobsToComplete = true;
            });

            return builder;
        }
    }
}
