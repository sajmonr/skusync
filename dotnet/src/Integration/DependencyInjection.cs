using AWS.Messaging.Configuration;
using Integration.Aws;
using Integration.Aws.Sqs;
using Integration.Shopify.GraphQl;
using Integration.Shopify.Products;
using Integration.Skulabs.Items;
using Integration.Skulabs.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Options;
using ShopifySharp.Extensions.DependencyInjection;

namespace Integration;

public static class DependencyInjection
{
    
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        public T AddIntegration()
        {
            // Skulabs
            // builder.AddOptionsFromConfiguration<SkulabsApiOptions>(SkulabsApiOptions.SectionKey);
            builder.Services.AddHttpClient<ItemClient>();
            
            // Shopify
            builder.AddOptionsFromConfiguration<ShopifyOptions>(ShopifyOptions.SectionKey);
            builder.Services.AddTransient<IShopifyGraphQlService, ShopifyGraphQlService>();
            builder.Services.AddTransient<IShopifyProductService, ShopifyProductService>();
            
            builder.Services.AddShopifySharpServiceFactories();
            
            // AWS SQS
            var awsAuthConfig = builder.GetRequiredConfigValue<AwsAuthOptions>(AwsAuthOptions.OptionsKey);
            var sqsOptions = builder.GetRequiredConfigValue<SqsOptions>(SqsOptions.OptionsKey);

            builder.Services.AddDefaultAWSOptions(awsAuthConfig.GetSetupOptions());
            builder.Services.AddAWSMessageBus(busBuilder =>
            {
                // The poller is tied to a single message type and expects raw JSON.
                // This is a good fit for a queue dedicated to a single AWS service event.
                busBuilder.AddSQSPoller<SqsShopEventProductMessage>(
                    sqsOptions.QueueUrl,
                    messageEnvelopeMode: MessageEnvelopeMode.NotSupported, options: options => {
                        options.MaxNumberOfConcurrentMessages = 1;
                    });
    
                busBuilder.AddMessageHandler<SqsShopEventProductHandler, SqsShopEventProductMessage>();
            });
            
            return builder;
        }

    }
    
}