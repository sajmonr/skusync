using Application;
using FastEndpoints;
using Hosting;
using Infrastructure;
using Infrastructure.Database;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog;
using SharedKernel.Options;
using Web.Api;
using Web.Api.Common;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
var corsOptions = builder.GetRequiredConfigValue<DashboardCorsOptions>(DashboardCorsOptions.SectionName);
builder.Services.AddCors(options => options.AddPolicy("dashboard", policy => policy
    .WithOrigins(corsOptions.GetSanitizedOrigins())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// Add Serilog
builder.Host.UseSerilog((context, loggerConfig) => loggerConfig.ReadFrom.Configuration(context.Configuration));

// Web.Api serves HTTP only — SQS webhook consumption, Shopify webhook handlers, and the Hangfire
// recurring jobs all belong to AppServer. Web.Api enqueues background work (the manual full sync)
// through the Hangfire client on shared Postgres storage and calls the sync services directly for
// synchronous work (the per-item manual sync).
builder.AddInfrastructure()
    .AddApplication()
    .AddPresentation();

var dashboardAuthenticationOptions = builder.GetRequiredConfigValue<DashboardAuthenticationOptions>(
    DashboardAuthenticationOptions.SectionName);
dashboardAuthenticationOptions.Validate(builder.Environment);

builder.Services.AddSingleton(dashboardAuthenticationOptions);
builder.Services.AddSingleton<DashboardPasswordValidator>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // The dashboard and API are served over plain HTTP in local development, where browsers
        // refuse to store a "__Host-"/Secure cookie — so login would appear to succeed but the
        // session cookie would never persist. Relax the cookie in Development only; production
        // keeps the hardened "__Host-" prefix and always-Secure policy.
        var isDevelopment = builder.Environment.IsDevelopment();
        options.Cookie.Name = isDevelopment ? "skusync-dashboard" : "__Host-skusync-dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = isDevelopment
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(dashboardAuthenticationOptions.SessionDurationHours);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAssertion((Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context) =>
            dashboardAuthenticationOptions.IsBypassed(builder.Environment) ||
            context.User.Identity?.IsAuthenticated == true)
        .Build();
});
builder.Services.AddDashboardLoginRateLimiting();
builder.Services.AddProductSyncRateLimiting();

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
app.UseCors("dashboard");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseFastEndpoints(configuration =>
{
    configuration.Endpoints.Configurator = endpoint => endpoint.Options(options => options.RequireAuthorization());
    configuration.Binding.UsePropertyNamingPolicy = true;
    configuration.Errors.ContentType = ApiDefaults.ProblemDetailsContentType;
    configuration.Errors.ProducesMetadataType = typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails);
    configuration.Errors.ResponseBuilder = ApiProblemDetails.CreateValidationResponse;
});

await app.RunAsync();

public partial class Program;
