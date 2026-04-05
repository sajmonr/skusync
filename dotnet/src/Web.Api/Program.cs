using Application;
using Application.Shopify;
using HealthChecks.UI.Client;
using Infrastructure;
using Infrastructure.Database;
using Integration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
    .AddInfrastructure()
    .AddApplication()
    .AddPresentation();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Swagger"));
}

app.ApplyDatabaseMigrations();

app.MapHealthChecks("_health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapGet("/shopifysync", async (IShopifySyncService syncService) =>
{
    await syncService.SynchronizeProducts();
    
    return Results.Ok();
});

app.UseSerilogRequestLogging();

app.Run();
