using Application.Jobs;
using Hangfire;
using Application.Products.Services;
using Application.Products.Webhook;
using Application.Skulabs.Services;
using Application.Skus;
using Application.Sync;
using Application.Sync.Merge;
using Application.Sync.Merge.Rules;
using Integration;
using Integration.Aws.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using SharedKernel.Options;

namespace Application;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers Application-layer services and their supporting configuration. Postgres is
        /// the only coordination layer — there is no message broker; cross-host work (Web.Api →
        /// AppServer) goes through Hangfire on shared storage, and the sync pipeline coordinates
        /// through the pending-sync flags on the mirror rows.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddApplication()
        {
            builder.AddIntegration();
            builder.Services.AddFeatureManagement();
            builder.AddOptionsFromConfiguration<SkuGeneratorOptions>(
                SkuGeneratorOptions.SectionKey
            );

            builder.Services.AddTransient<IProductsService, ProductsService>();
            builder.Services.AddTransient<ISkulabsItemSyncService, SkulabsItemSyncService>();
            builder.Services.AddTransient<ISkuGenerator, SkuGenerator>();
            builder.Services.AddTransient<IReconciler, Reconciler>();

            // Field authority, one rule per field. Registration order is deliberately not
            // significant: the chain validates on construction that no two rules claim the same
            // field, so adding one here cannot quietly change what an existing field resolves to.
            builder.Services.AddTransient<IMergeRule, SkuMergeRule>();
            builder.Services.AddTransient<IMergeRule, BarcodeMergeRule>();
            builder.Services.AddTransient<IMergeRule, TitleMergeRule>();
            builder.Services.AddTransient<IMergeRule, LocationMergeRule>();
            builder.Services.AddTransient<MergeRuleChain>();

            builder.Services.AddTransient<IShopifyDispatcher, ShopifyDispatcher>();
            builder.Services.AddTransient<ISkulabsDispatcher, SkulabsDispatcher>();
            builder.Services.AddTransient<IShopifyDispatchTrigger, ShopifyDispatchTrigger>();

            // Hangfire client: every host that has the Application layer can enqueue background
            // jobs (Web.Api enqueues; AppServer both enqueues and processes). Processing is a
            // separate opt-in — see AddHangfireProcessing.
            var hangfireConnection = builder.Configuration.GetConnectionString(
                HangfireConfiguration.ConnectionStringName
            );
            if (string.IsNullOrWhiteSpace(hangfireConnection))
            {
                throw new InvalidOperationException(
                    $"A '{HangfireConfiguration.ConnectionStringName}' connection string is required for "
                        + "Hangfire background-job storage."
                );
            }

            builder.Services.AddHangfire(config =>
                HangfireConfiguration.Configure(config, hangfireConnection)
            );

            return builder;
        }

        /// <summary>
        /// Registers the Hangfire background-job <em>server</em> that dequeues and executes jobs from
        /// shared storage. Only hosts responsible for processing jobs (currently AppServer) should
        /// call this, and only after <see cref="AddApplication"/>, which establishes the Hangfire
        /// client and storage.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddHangfireProcessing()
        {
            builder.Services.AddHangfireServer();

            builder.Services.AddTransient<RecurringJobs>();
            builder.Services.AddTransient<FullSyncOrchestrator>();
            builder.Services.AddTransient<SingleItemSyncJob>();
            builder.AddOptionsFromConfiguration<ScheduledJobsOptions>(
                ScheduledJobsOptions.SectionKey
            );
            builder.Services.AddHostedService<RecurringJobRegistrar>();

            // The short-tick drain that gets a change out in seconds. The recurring jobs above stay
            // as the failsafe: everything here is an optimisation over them, so their cadence can be
            // relaxed but not removed.
            builder.AddOptionsFromConfiguration<SyncDrainLoopOptions>(SyncDrainLoopOptions.SectionKey);
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.AddHostedService<SyncDrainLoop>();

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
            builder.Services.AddTransient<
                IShopifyWebhookHandler,
                ShopifyProductDeleteWebhookHandler
            >();

            return builder;
        }

    }
}
