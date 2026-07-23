using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.Api.Features.Authentication.Logout;

public class LogoutHandler : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("auth/logout");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        await SignOutAsync();
        await Send.NoContentAsync(cancellationToken);
    }

    private Task SignOutAsync() =>
        HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
