using System.ComponentModel.DataAnnotations;

namespace Integration.Skulabs.Options;

/// <summary>
/// Represents configuration options for the Skulabs API integration.
/// </summary>
/// <remarks>
/// This class is used to configure and validate the required settings for connecting to the Skulabs API.
/// The options can be loaded from a configuration source, such as appsettings.json, using the section key "Skulabs:Api".
/// </remarks>
public sealed class SkulabsApiOptions
{

    public const string SectionKey = "Skulabs:Api";

    /// <summary>
    /// Gets the base URL for the Skulabs API.
    /// </summary>
    /// <remarks>
    /// This property is required and specifies the root address of the Skulabs API endpoint.
    /// It is used as the base URI for API requests.
    /// Ensure this property is configured with a valid URL.
    /// </remarks>
    [Required]
    public string BaseUrl { get; init; } = "";

    /// <summary>
    /// Gets the API key for authenticating requests to the Skulabs API.
    /// </summary>
    /// <remarks>
    /// This property is mandatory and is used to authorize access to the Skulabs API.
    /// It must be set to a valid API key issued by Skulabs. Ensure that the key is kept
    /// secure and not exposed in publicly accessible locations.
    /// </remarks>
    [Required]
    public string ApiKey { get; init; } = "";

}