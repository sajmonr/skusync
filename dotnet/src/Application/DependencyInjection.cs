using Application.Shopify;
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
            return builder;
        }
    }
}