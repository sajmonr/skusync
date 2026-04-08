using System.ComponentModel.DataAnnotations;

namespace Application.Jobs;

public class ScheduledJobsOptions
{
    public const string SectionKey = "ScheduledJobs";

    [Required]
    public JobScheduleOptions ShopifyProductSync { get; init; } = new();
}

public class JobScheduleOptions
{
    public string CronExpression { get; init; } = "0 0 * * * ?";
    public bool RunOnStart { get; init; }
    public bool Enabled { get; init; } = true;
}
