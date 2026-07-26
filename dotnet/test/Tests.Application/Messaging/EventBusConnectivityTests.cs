using global::Application;
using Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Tests.Application.Messaging;

public class EventBusConnectivityTests
{
    [Fact]
    public async Task VerifyEventBusConnectivity_ShouldThrow_WhenBrokerIsUnreachable()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // Port 1 has no broker listening, so the connection is refused immediately rather
                // than hanging — exactly the "broker present in config but unreachable" case we
                // must fail on. Wired through the real AddApplication registration.
                ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@127.0.0.1:1",
                // AddApplication also requires the Hangfire (Postgres) connection string.
                ["ConnectionStrings:SkuSync"] =
                    "Host=localhost;Database=skusync;Username=postgres;Password=password",
            }
        );
        builder.AddApplication();
        using var host = builder.Build();

        await Should.ThrowAsync<Exception>(() => host.VerifyEventBusConnectivity());
    }
}
