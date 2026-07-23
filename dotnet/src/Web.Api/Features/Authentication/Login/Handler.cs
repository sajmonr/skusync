using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.Api.Features.Authentication.Login;

public class LoginHandler(DashboardPasswordValidator passwordValidator) : Endpoint<LoginRequest>
{
    public override void Configure()
    {
        Post("auth/login");
        AllowAnonymous();
        Options(endpoint => endpoint.RequireRateLimiting(DashboardLoginRateLimitingExtensions.PolicyName));
    }

    public override async Task HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (!passwordValidator.IsValid(request.Password))
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        await SignInAsync();
        await Send.NoContentAsync(cancellationToken);
    }

    private Task SignInAsync() => HttpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        CreateDashboardPrincipal(),
        new AuthenticationProperties { IsPersistent = true });

    private static ClaimsPrincipal CreateDashboardPrincipal() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "dashboard")],
        CookieAuthenticationDefaults.AuthenticationScheme));

}
