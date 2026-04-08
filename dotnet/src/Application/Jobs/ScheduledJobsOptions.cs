using System.ComponentModel.DataAnnotations;

namespace Application.Jobs;

/// <summary>
/// Root options class for all scheduled job configuration, bound to the <c>ScheduledJobs</c>
/// configuration section. Add a new <see cref="JobScheduleOptions"/> property here for each
/// additional job introduced to the application.
/// </summary>
public class ScheduledJobsOptions
{
    /// <summary>The configuration section key used to bind this options class.</summary>
    public const string SectionKey = "ScheduledJobs";

    /// <summary>
    /// Gets the schedule configuration for the Shopify product synchronization job.
    /// </summary>
    [Required]
    public JobScheduleOptions ShopifyProductSync { get; init; } = new();
}

/// <summary>
/// Defines the scheduling behaviour for a single Quartz.NET job. Each job reads its own
/// instance of this class from a named subsection of <c>ScheduledJobs</c>.
/// </summary>
public class JobScheduleOptions
{
    /// <summary>
    /// Gets the Quartz cron expression that controls when the job fires.
    /// Defaults to <c>0 0 * * * ?</c> (top of every hour).
    /// </summary>
    public string CronExpression { get; init; } = "0 0 * * * ?";

    /// <summary>
    /// Gets a value indicating whether the job should fire immediately when the
    /// application starts, in addition to its regular cron schedule.
    /// </summary>
    public bool RunOnStart { get; init; }

    /// <summary>
    /// Gets a value indicating whether this job is enabled. When <c>false</c> the job is
    /// not registered with the Quartz scheduler at all and will never execute.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
