namespace SharedKernel.Options;

/// <summary>
/// Exception thrown when a required configuration section or connection string is absent
/// from the application configuration. This is typically raised during application startup
/// to surface missing configuration early rather than encountering null-reference errors at runtime.
/// </summary>
public class OptionsConfigurationSectionNotFoundException(string configurationKey)
    : Exception(GetMessage(configurationKey))
{
    private static string GetMessage(string configurationKey) =>
        $"The configuration section '{configurationKey}' was not found.";
}
