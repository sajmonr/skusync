using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.Api.Features.Authentication;

public class LoginEndpoint(DashboardAuthenticationOptions options) : Endpoint<LoginRequest>
{
    public override void Configure()
    {
        Post("auth/login");
        AllowAnonymous();
        Options(endpoint => endpoint.RequireRateLimiting(DashboardLoginRateLimitingExtensions.PolicyName));
    }

    public override async Task HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (!options.IsBypassed(HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()) &&
            !PasswordsMatch(request.Password, options.Password))
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "dashboard")],
            CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });
        await Send.NoContentAsync(cancellationToken);
    }

    private static bool PasswordsMatch(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}

public class SessionEndpoint(DashboardAuthenticationOptions options) : EndpointWithoutRequest<SessionResponse>
{
    public override void Configure()
    {
        Get("auth/session");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken) =>
        await Send.OkAsync(
            new SessionResponse(
                options.IsBypassed(HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()) ||
                HttpContext.User.Identity?.IsAuthenticated == true),
            cancellationToken);
}

public class LogoutEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("auth/logout");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await Send.NoContentAsync(cancellationToken);
    }
}

public class LoginRequest
{
    public string Password { get; init; } = "";
}

public readonly record struct SessionResponse(bool IsAuthenticated);
