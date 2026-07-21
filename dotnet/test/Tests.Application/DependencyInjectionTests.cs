using global::Application;
using Application.Jobs.Maintenance;
using Application.Products.Services;
using Integration.Aws.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using SlimMessageBus;

namespace Tests.Application;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_ShouldRegisterCoreServicesWithoutProcessingInfrastructure()
    {
        var builder = CreateBuilder();

        builder.AddApplication();

        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IProductsService)
        );
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IShopifyWebhookHandler)
        );
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IMessageBus)
        );
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IMaintenanceTask)
        );
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
        );
    }

    [Fact]
    public void AddShopifyWebhookHandlers_ShouldRegisterBothHandlersWithoutHostedServices()
    {
        var builder = CreateBuilder();

        builder.AddWebhookProcessing();

        builder
            .Services.Count(descriptor => descriptor.ServiceType == typeof(IShopifyWebhookHandler))
            .ShouldBe(2);
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
        );
    }

    [Fact]
    public void AddInMemoryEventProcessing_ShouldRegisterMessageBus()
    {
        var builder = CreateBuilder();

        builder.AddEventProcessing();

        builder.Services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IMessageBus));
    }

    [Fact]
    public void AddScheduledJobs_ShouldRegisterMaintenanceTasksAndHostedScheduler()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string?>
            {
                ["ScheduledJobs:SkulabsItemSync:Enabled"] = "false",
                ["ScheduledJobs:ProductMaintenance:Enabled"] = "false",
            }
        );

        builder.AddScheduledJobs();

        builder
            .Services.Count(descriptor => descriptor.ServiceType == typeof(IMaintenanceTask))
            .ShouldBe(3);
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
