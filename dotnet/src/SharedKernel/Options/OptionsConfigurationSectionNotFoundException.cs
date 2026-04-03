namespace SharedKernel.Options;

public class OptionsConfigurationSectionNotFoundException(string configurationKey)
: Exception(GetMessage(configurationKey))
{
    
    private static string GetMessage(string configurationKey) => $"The configuration section '{configurationKey}' was not found.";
    
}