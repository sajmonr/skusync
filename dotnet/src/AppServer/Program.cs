using Hosting;
using Infrastructure.Database;
using Microsoft.AspNetCore.Builder;
using Serilog;

namespace AppServer;

// Explicit (namespaced) entry point rather than top-level statements: the implicit top-level
// Program is a public global type, which would collide with Web.Api's public Program in the E2E
// test project that references both hosts. AppServer's host is composed in tests via AddAppServer,
// so it never needs a public Program of its own.
internal static class Program
{
    private static async Task Main(string[] args)
    {
        // AppServer is a background worker, but it hosts a minimal HTTP surface purely to expose
        // health endpoints (/_health/live, /_health/ready) so Docker/Dokploy can probe it —
        // mirroring Web.Api. WebApplicationBuilder still honours DOTNET_ENVIRONMENT (the variable a
        // worker reads), so the existing launch profile and compose configuration keep working.
        var builder = WebApplication.CreateBuilder(args);

        // Route logging through Serilog, reading sinks and levels from configuration.
        builder.Services.AddSerilog(
            (_, loggerConfig) => loggerConfig.ReadFrom.Configuration(builder.Configuration)
        );

        // AppServer owns all background processing: SQS webhook consumption, Shopify webhook
        // handlers, and the Hangfire recurring jobs. Web.Api registers none of these — it serves
        // HTTP only.
        builder.AddAppServer();

        var app = builder.Build();

        // Run coordinated migrations before hosted services (SQS poller, Hangfire) start. The
        // Postgres advisory lock ensures Web.Api and AppServer never migrate concurrently.
        await app.ApplyDatabaseMigrations();

        app.MapHealthCheckEndpoints();

        await app.RunAsync();
    }
}
