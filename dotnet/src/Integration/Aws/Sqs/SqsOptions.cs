using System.ComponentModel.DataAnnotations;

namespace Integration.Aws.Sqs;

public sealed class SqsOptions
{

    public const string OptionsKey = "Aws:Sqs";

    [Required] public string QueueUrl { get; init; } = "";
}