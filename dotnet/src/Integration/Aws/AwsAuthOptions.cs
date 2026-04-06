using System.ComponentModel.DataAnnotations;
using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;

namespace Integration.Aws;

public sealed class AwsAuthOptions
{
    
    public const string OptionsKey = "Aws:Auth";

    [Required] public string Region { get; init; } = string.Empty;

    [Required]
    public string AccessKey { get; init; } = string.Empty;

    [Required]
    public string SecretKey { get; init; } = string.Empty;
    
    public AWSOptions GetSetupOptions()
    {
        return new AWSOptions
        {
            Region = RegionEndpoint.GetBySystemName(Region),
            Credentials = new BasicAWSCredentials(AccessKey, SecretKey)
        };
    }
}