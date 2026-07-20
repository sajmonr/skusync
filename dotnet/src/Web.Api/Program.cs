using Application;
using Infrastructure;
using Infrastructure.Database;
using Integration;
using Integration.Shopify.Products;
using Serilog;
using Web.Api;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Serilog
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Add application parts
builder.AddIntegration()
    .AddSqsWebhookConsumer()
    .AddInfrastructure()
    .AddApplication()
    .AddShopifyWebhookHandlers()
    .AddInMemoryEventProcessing()
    .AddScheduledJobs()
    .AddPresentation();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Swagger"));
}

await app.ApplyDatabaseMigrations();

app.MapHealthCheckEndpoints();

app.UseSerilogRequestLogging();

await app.RunAsync();

public partial class Program;
