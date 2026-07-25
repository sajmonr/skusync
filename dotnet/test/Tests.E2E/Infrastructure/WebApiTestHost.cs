using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Web.Api;

namespace Tests.E2E.Infrastructure;

public class WebApiTestHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    // Web.Api calls AddApplication, which registers the event producers and starts the bus, so the
    // host needs a reachable broker to boot even though these tests exercise only the HTTP surface.
    private readonly RabbitMqContainer rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();
    private readonly Dictionary<string, string?> originalEnvironmentValues = [];

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await rabbitMq.StartAsync();

        SetEnvironmentVariable("ConnectionStrings__SkuSync", postgres.GetConnectionString());
        SetEnvironmentVariable("ConnectionStrings__RabbitMq", rabbitMq.GetConnectionString());
        SetEnvironmentVariable("DashboardAuthentication__Password", "test-password");
        SetEnvironmentVariable("DashboardAuthentication__BypassOnDevelopment", "false");
    }

    public new async Task DisposeAsync()
    {
        RestoreEnvironmentVariables();
        await rabbitMq.DisposeAsync();
        await postgres.DisposeAsync();
        Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        originalEnvironmentValues[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private void RestoreEnvironmentVariables()
    {
        foreach (var (key, value) in originalEnvironmentValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
