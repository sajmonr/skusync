using FastEndpoints;
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

// Web.Api serves HTTP only. All background processing — SQS webhook consumption, Shopify
// webhook handlers, in-memory event consumers, and scheduled jobs — is owned by AppServer,
// so this host registers none of it and requires no SQS/queue configuration to start.
builder.AddInfrastructure()
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
        options.Cookie.Name = "__Host-skusync-dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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
