using FastEndpoints;
using Infrastructure;
using Infrastructure.Database;
using Serilog;
using Web.Api;
using Web.Api.Common;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Serilog
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Web.Api serves HTTP only. All background processing — SQS webhook consumption, Shopify
// webhook handlers, in-memory event consumers, and scheduled jobs — is owned by AppServer,
// so this host registers none of it and requires no SQS/queue configuration to start.
builder.AddInfrastructure()
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
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseFastEndpoints(configuration =>
{
    configuration.Endpoints.RoutePrefix = ApiDefaults.RoutePrefix;
    configuration.Binding.UsePropertyNamingPolicy = true;
    configuration.Errors.ContentType = ApiDefaults.ProblemDetailsContentType;
    configuration.Errors.ProducesMetadataType = typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails);
    configuration.Errors.ResponseBuilder = ApiProblemDetails.CreateValidationResponse;
});

await app.RunAsync();

public partial class Program;
