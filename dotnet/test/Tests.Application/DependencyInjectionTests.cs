using global::Application;
using Application.Jobs;
using Application.Products.Services;
using Application.Sync;
using Integration.Aws.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Tests.Application;

public class DependencyInjectionTests
{
    private const string SkuSyncConnectionString =
        "Host=localhost;Database=skusync;Username=postgres;Password=password";

    [Fact]
    public void AddApplication_ShouldRegisterCoreServices()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:SkuSync"] = SkuSyncConnectionString,
            }
        );

        builder.AddApplication();

        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IProductsService)
        );
        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IReconciler)
        );
        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IShopifyDispatcher)
        );
        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(ISkulabsDispatcher)
        );
        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IShopifyDispatchTrigger)
        );
        // Webhook processing infrastructure is not — that is a separate concern.
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IShopifyWebhookHandler)
        );
    }

    [Fact]
    public void AddApplication_ShouldThrow_WhenSkuSyncConnectionStringIsMissing()
    {
        var builder = CreateBuilder();

        Should.Throw<InvalidOperationException>(() => builder.AddApplication());
    }

    [Fact]
    public void AddShopifyWebhookHandlers_ShouldRegisterAllHandlersWithoutHostedServices()
    {
        var builder = CreateBuilder();

        builder.AddWebhookProcessing();

        builder
            .Services.Count(descriptor => descriptor.ServiceType == typeof(IShopifyWebhookHandler))
            .ShouldBe(3);
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
        );
    }

    [Fact]
    public void AddHangfireProcessing_ShouldRegisterRecurringJobsAndScheduler()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:SkuSync"] = SkuSyncConnectionString,
            }
        );

        builder.AddApplication();
        builder.AddHangfireProcessing();

        builder.Services.ShouldContain(descriptor => descriptor.ServiceType == typeof(RecurringJobs));
        // The Hangfire server and the recurring-job registrar are hosted services.
        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
        );
    }

    private static IHostApplicationBuilder CreateBuilder(
        IReadOnlyDictionary<string, string?>? settings = null
    )
    {
        var configuration = new ConfigurationManager();
        if (settings is not null)
        {
            configuration.AddInMemoryCollection(settings);
        }

        var builder = Substitute.For<IHostApplicationBuilder>();
        builder.Configuration.Returns(configuration);
        builder.Services.Returns(new ServiceCollection());

        return builder;
    }
}
