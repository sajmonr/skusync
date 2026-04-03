using System.ComponentModel.DataAnnotations;

namespace Integration.Shopify.Options;

/// <summary>
/// Represents configuration options required to connect to a Shopify store.
/// </summary>
public class ShopifyOptions
{
    
    public const string SectionKey = "Shopify";
    
    /// <summary>
    /// Gets the URL of the Shopify store. This is used to establish a connection
    /// to the specified store for executing API calls and integrations.
    /// </summary>
    /// <remarks>
    /// The value of this property is required and should be a fully qualified URL
    /// pointing to the Shopify store.
    /// </remarks>
    [Required]
    public required string ShopUrl { get; init; }

    /// <summary>
    /// Gets or initializes the API key required for authenticating with the Shopify store.
    /// This value is mandatory and must be provided to enable successful integration.
    /// </summary>
    [Required]
    public required string ApiKey { get; init; }

}