using AWS.Messaging.Configuration;
using Integration.Aws;
using Integration.Aws.Sqs;
using Integration.RateLimiting;
using Integration.Shopify.GraphQl;
using Integration.Shopify.Products;
using Integration.Skulabs.Items;
using Integration.Skulabs.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using SharedKernel.Options;
using ShopifySharp.Extensions.DependencyInjection;

namespace Integration;

public static class DependencyInjection
{
    
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers Integration-layer outbound clients and their supporting services with the
        /// dependency injection container. This method does not start hosted message consumers.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddIntegration()
        {
            // Rate limiting (shared across integration clients).
            builder.Services.AddMemoryCache();
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IRateLimitService, InMemoryRateLimitService>();

            // Skulabs
            builder.AddOptionsFromConfiguration<SkulabsApiOptions>(SkulabsApiOptions.SectionKey);
            builder.Services.AddTransient<SkulabsRateLimitHandler>();
            builder.Services.AddHttpClient<ISkulabsItemClient, SkulabsItemClient>()
                // Outer-most: short-circuits when a cooldown is in effect, and records the cooldown
                // after the resilience pipeline has produced its final 429 (so the very first
                // transient 429 doesn't trip the breaker — only persistent rate limits do).
                .AddHttpMessageHandler<SkulabsRateLimitHandler>()
                .AddResilienceHandler("skulabs-retry", SkulabsResiliencePipeline.Configure);
            
            // Shopify
            builder.AddOptionsFromConfiguration<ShopifyOptions>(ShopifyOptions.SectionKey);
            builder.Services.AddTransient<IShopifyGraphQlService, ShopifyGraphQlService>();
            builder.Services.AddTransient<IShopifyProductService, ShopifyProductService>();
            
            builder.Services.AddShopifySharpServiceFactories();

            return builder;
        }

        /// <summary>
        /// Registers the AWS SQS poller that receives Shopify webhook messages and dispatches
        /// them to the configured message handler. Only hosts responsible for webhook processing
        /// should call this method.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddSqsWebhookConsumer()
        {
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
