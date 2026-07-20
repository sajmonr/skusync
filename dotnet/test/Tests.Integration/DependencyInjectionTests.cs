using global::Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Tests.Integration;

public class DependencyInjectionTests
{
    [Fact]
    public void AddIntegration_ShouldNotRequireSqsConfigurationOrRegisterHostedServices()
    {
        var builder = CreateBuilder();

        Should.NotThrow(builder.AddIntegration);

        builder.Services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddSqsWebhookConsumer_ShouldRegisterHostedService()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Aws:Auth:Region"] = "us-east-1",
            ["Aws:Auth:AccessKey"] = "access-key",
            ["Aws:Auth:SecretKey"] = "secret-key",
            ["Aws:Sqs:QueueUrl"] = "https://sqs.us-east-1.amazonaws.com/123456789012/shopify-webhooks"
        });

        builder.AddSqsWebhookConsumer();

        builder.Services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService));
    }

    private static IHostApplicationBuilder CreateBuilder(
        IReadOnlyDictionary<string, string?>? settings = null)
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
