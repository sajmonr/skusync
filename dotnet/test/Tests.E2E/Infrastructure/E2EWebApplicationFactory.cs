using Integration.Shopify.GraphQl;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Testcontainers.PostgreSql;
using WireMock.Server;

namespace Tests.E2E.Infrastructure;

/// <summary>
/// Boots the real Web.Api host against a Testcontainers Postgres + WireMock + a substituted
/// Shopify GraphQL client. Shared across all tests in a class via IClassFixture so the
/// container + WireMock cost is paid once.
/// </summary>
/// <remarks>
/// Why substitute IShopifyGraphQlService instead of pointing ShopifySharp at WireMock?
/// ShopifySharp hardcodes https://{shop}/admin/... and builds its own HttpClient, so
/// redirecting it to WireMock requires HTTPS + custom cert handling that doesn't add coverage
/// of our code. IShopifyGraphQlService is the seam between our code and the Shopify SDK —
/// substituting it lets ShopifyProductService.UpdateVariants run for real and lets us assert
/// on the GraphQL query and variables it produces.
/// </remarks>
public class E2EWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18.3")
        .Build();

    /// <summary>Local WireMock server for outbound HTTP we own (Skulabs). Started per-fixture.</summary>
    public WireMockServer WireMock { get; private set; } = null!;

    /// <summary>Substituted Shopify GraphQL client. Configure return values + assert calls from tests.</summary>
    public IShopifyGraphQlService ShopifyGraphQl { get; } = Substitute.For<IShopifyGraphQlService>();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();
        WireMock = WireMockServer.Start();

        // Program.cs reads configuration values during builder.AddInfrastructure() — before
        // WebApplicationFactory's ConfigureAppConfiguration callbacks would normally run. The
        // reliable seam is environment variables: WebApplication.CreateBuilder includes them
        // in its default configuration. Double underscores map to colons in .NET config keys.
        var skulabsBase = WireMock.Url!;
        SetEnv("ConnectionStrings__SkuSync", _postgres.GetConnectionString());
        SetEnv("Shopify__ShopUrl", "https://e2e-test.myshopify.com");
        SetEnv("Shopify__ApiKey", "test-shopify-token");
        SetEnv("Skulabs__Api__BaseUrl", skulabsBase);
        SetEnv("Skulabs__Api__ApiKey", "test-skulabs-key");
        SetEnv("Skulabs__Admin__BaseUrl", skulabsBase);
        SetEnv("Skulabs__Admin__Username", "test");
        SetEnv("Skulabs__Admin__Password", "test");
        SetEnv("Aws__Auth__AccessKey", "test");
        SetEnv("Aws__Auth__SecretKey", "test");
        SetEnv("Aws__Auth__Region", "us-east-1");
        SetEnv("Aws__Sqs__QueueUrl", "https://localhost/test-queue");
        SetEnv("FeatureManagement__ShopifyWriteBack", "true");
        SetEnv("ScheduledJobs__ShopifyProductSync__Enabled", "false");
        SetEnv("ScheduledJobs__ShopifyProductSync__RunOnStart", "false");
        SetEnv("ScheduledJobs__ShopifyProductSync__CronExpression", "0 0 0 * * ?");

        // Force the host to build now so DI overrides apply and migrations run against the container.
        _ = Services;
    }

    private static void SetEnv(string key, string value) =>
        Environment.SetEnvironmentVariable(key, value);

    async Task IAsyncLifetime.DisposeAsync()
    {
        WireMock.Stop();
        WireMock.Dispose();
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace the real Shopify GraphQL client with a substitute so we can capture and
            // assert on outbound calls without going through ShopifySharp/HTTPS.
            services.RemoveAll<IShopifyGraphQlService>();
            services.AddSingleton(ShopifyGraphQl);

            // Disable background services we don't want in tests:
            //   - Quartz scheduler (would fire cron jobs)
            //   - AWS SQS poller (would connect to real AWS)
            // Webhook handlers and consumers are invoked directly or via the in-memory bus.
            RemoveHostedServicesByTypeName(services, "Quartz", "AWS.Messaging", "MessagePump");
        });
    }

    private static void RemoveHostedServicesByTypeName(IServiceCollection services, params string[] needles)
    {
        var toRemove = services
            .Where(s => s.ServiceType == typeof(IHostedService))
            .Where(s => s.ImplementationType?.FullName is { } fn
                        && needles.Any(n => fn.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var descriptor in toRemove)
        {
            services.Remove(descriptor);
        }
    }
}
