using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.Api.Features.Authentication.Login;

public class LoginHandler(DashboardAuthenticationOptions options) : Endpoint<LoginRequest>
{
    public override void Configure()
    {
        Post("auth/login");
        AllowAnonymous();
        Options(endpoint => endpoint.RequireRateLimiting(DashboardLoginRateLimitingExtensions.PolicyName));
    }

    public override async Task HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (!IsPasswordValid(request.Password))
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        await SignInAsync();
        await Send.NoContentAsync(cancellationToken);
    }

    private bool IsPasswordValid(string password) =>
        options.IsBypassed(HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()) ||
        PasswordsMatch(password, options.Password);

    private Task SignInAsync() => HttpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        CreateDashboardPrincipal(),
        new AuthenticationProperties { IsPersistent = true });

    private static ClaimsPrincipal CreateDashboardPrincipal() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "dashboard")],
        CookieAuthenticationDefaults.AuthenticationScheme));

    private static bool PasswordsMatch(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
