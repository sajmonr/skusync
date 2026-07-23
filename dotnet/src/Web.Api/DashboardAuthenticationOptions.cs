using Microsoft.Extensions.Hosting;

namespace Web.Api;

public class DashboardAuthenticationOptions
{
    public const string SectionName = "DashboardAuthentication";

    public string Password { get; init; } = "";

    public bool BypassOnDevelopment { get; init; }

    public int SessionDurationHours { get; init; } = 12;

    public bool IsBypassed(IHostEnvironment environment) =>
        environment.IsDevelopment() && BypassOnDevelopment;

    public void Validate(IHostEnvironment environment)
    {
        if (SessionDurationHours is < 1 or > 168)
        {
            throw new InvalidOperationException(
                $"{SectionName}:SessionDurationHours must be between 1 and 168.");
        }

        if (!IsBypassed(environment) && string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Password must be configured unless development authentication is bypassed.");
        }
    }
}
