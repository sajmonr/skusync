using Integration.Aws.Sqs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using SlimMessageBus;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

/// <summary>
/// Proves the AppServer host composes in all the background-processing workloads it owns.
/// Assertions run against <see cref="AppServerTestHost.RegisteredServices"/> — the
/// registration snapshot taken before test-only removals — so we can confirm the SQS poller
/// and Quartz scheduler are wired up without those hosted services actually running against
/// real AWS or firing cron jobs.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class AppServerCompositionTests(AppServerTestHost factory)
{
    [Fact]
    public void AppServer_RegistersSqsPollerHostedService()
    {
        HostedServiceImplementationNames()
            .ShouldContain(name =>
                name.Contains("AWS.Messaging", StringComparison.OrdinalIgnoreCase)
                || name.Contains("MessagePump", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppServer_RegistersQuartzHostedService()
    {
        HostedServiceImplementationNames()
            .ShouldContain(name => name.Contains("Quartz", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppServer_RegistersInMemoryEventBus()
    {
        factory.RegisteredServices.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IMessageBus));
    }

    [Fact]
    public void AppServer_RegistersShopifyWebhookHandlers()
    {
        factory.RegisteredServices
            .Count(descriptor => descriptor.ServiceType == typeof(IShopifyWebhookHandler))
            .ShouldBe(2);
    }

    private IEnumerable<string> HostedServiceImplementationNames() =>
        factory.RegisteredServices
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType?.FullName)
            .Where(name => name is not null)
            .Select(name => name!);
}
