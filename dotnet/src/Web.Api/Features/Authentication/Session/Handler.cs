using FastEndpoints;

namespace Web.Api.Features.Authentication.Session;

public class SessionHandler(DashboardAuthenticationOptions options) : EndpointWithoutRequest<SessionResponse>
{
    public override void Configure()
    {
        Get("auth/session");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken) =>
        await Send.OkAsync(CreateResponse(), cancellationToken);

    private SessionResponse CreateResponse() => new(
        options.IsBypassed(HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()) ||
        HttpContext.User.Identity?.IsAuthenticated == true);
}
