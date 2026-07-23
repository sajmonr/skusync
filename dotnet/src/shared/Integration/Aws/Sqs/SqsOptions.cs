using System.ComponentModel.DataAnnotations;

namespace Integration.Aws.Sqs;

/// <summary>
/// Strongly-typed configuration options for the AWS SQS integration, bound to the
/// <c>Aws:Sqs</c> configuration section.
/// </summary>
public sealed class SqsOptions
{
    /// <summary>The configuration section key used to bind this options class.</summary>
    public const string OptionsKey = "Aws:Sqs";

    /// <summary>
    /// Gets the fully-qualified SQS queue URL that the application polls for Shopify
    /// shop-event messages, e.g.
    /// <c>https://sqs.us-east-1.amazonaws.com/123456789012/my-queue</c>.
    /// </summary>
    [Required]
    public string QueueUrl { get; init; } = "";
}
