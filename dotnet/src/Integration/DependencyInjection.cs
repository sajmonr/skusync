using Integration.Shopify.Options;
using Integration.Skulabs.Items;
using Integration.Skulabs.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Options;

namespace Integration;

public static class DependencyInjection
{
    
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        public T AddIntegration()
        {
            // Skulabs
            builder.AddOptionsFromConfiguration<SkulabsApiOptions>(SkulabsApiOptions.SectionKey);
            builder.Services.AddHttpClient<ItemClient>();
            
            // Shopify
            builder.AddOptionsFromConfiguration<ShopifyOptions>(ShopifyOptions.SectionKey);
            
            return builder;
        }
    }
    
}