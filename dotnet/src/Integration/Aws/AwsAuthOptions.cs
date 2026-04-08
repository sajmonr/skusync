using System.ComponentModel.DataAnnotations;
using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;

namespace Integration.Aws;

/// <summary>
/// Strongly-typed configuration options for AWS authentication, bound to the
/// <c>Aws:Auth</c> configuration section.
/// </summary>
public sealed class AwsAuthOptions
{
    /// <summary>The configuration section key used to bind this options class.</summary>
    public const string OptionsKey = "Aws:Auth";

    /// <summary>Gets the AWS region identifier, e.g. <c>us-east-1</c>.</summary>
    [Required]
    public string Region { get; init; } = string.Empty;

    /// <summary>Gets the AWS IAM access key ID used for programmatic authentication.</summary>
    [Required]
    public string AccessKey { get; init; } = string.Empty;

    /// <summary>Gets the AWS IAM secret access key paired with <see cref="AccessKey"/>.</summary>
    [Required]
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// Builds an <see cref="AWSOptions"/> instance populated from the current configuration values,
    /// ready to be passed to <c>AddDefaultAWSOptions</c>.
    /// </summary>
    /// <returns>A configured <see cref="AWSOptions"/> instance.</returns>
    public AWSOptions GetSetupOptions()
    {
        return new AWSOptions
        {
            Region = RegionEndpoint.GetBySystemName(Region),
            Credentials = new BasicAWSCredentials(AccessKey, SecretKey)
        };
    }
}
