namespace Web.Api;

public class DashboardCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = [];

    public string[] GetSanitizedOrigins() =>
        AllowedOrigins
            .Select(origin => origin.Trim())
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray();
}
