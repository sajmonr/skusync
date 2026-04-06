using Application.Queue.ShopifyProductUpdate;
using Application.Shopify;
using Integration.Aws.Sqs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            
            return builder;
        }
    }
}