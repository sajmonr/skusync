using global::Application;
using Application.Jobs.Maintenance;
using Application.Products.Events;
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
    private const string RabbitMqConnectionString = "amqp://guest:guest@localhost:5672";

    [Fact]
    public void AddApplication_ShouldRegisterCoreServicesAndEventProduction()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMq"] = RabbitMqConnectionString,
            }
        );

        builder.AddApplication();

        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IProductsService)
        );
        // Producing events is part of the Application layer, so the bus is registered here.
        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IMessageBus)
        );
        // Consumers and other processing infrastructure are not — those are separate concerns.
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IShopifyWebhookHandler)
        );
        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IMaintenanceTask)
        );
    }

    [Fact]
    public void AddApplication_ShouldThrow_WhenRabbitMqConnectionStringIsMissing()
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
    public void AddEventProcessing_ShouldRegisterEventConsumers()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMq"] = RabbitMqConnectionString,
            }
        );

        builder.AddApplication();
        builder.AddEventProcessing();

        builder.Services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IMessageBus));
        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(ShopifyVariantWritebackConsumer)
        );
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
