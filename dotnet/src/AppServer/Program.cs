using Application;
using Infrastructure;
using Infrastructure.Database;
using Integration;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Route logging through Serilog, reading sinks and levels from configuration.
builder.Services.AddSerilog(
    (_, loggerConfig) => loggerConfig.ReadFrom.Configuration(builder.Configuration)
);

// AppServer owns all background processing: SQS webhook consumption, Shopify webhook
// handlers, in-memory event consumers, and scheduled Quartz jobs. Web.Api registers none
// of these — it serves HTTP only. Keep this composition in sync with that division.
builder
    .AddIntegration()
    .AddSqsWebhookConsumer()
    .AddInfrastructure()
    .AddApplication()
    .AddWebhookProcessing()
    .AddEventProcessing()
    .AddScheduledJobs();

var host = builder.Build();

// Run coordinated migrations before hosted services (SQS poller, Quartz) start. The
// Postgres advisory lock ensures Web.Api and AppServer never migrate concurrently.
await host.ApplyDatabaseMigrations();

await host.RunAsync();
