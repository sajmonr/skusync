using System.ComponentModel.DataAnnotations;

namespace Application.Jobs;

/// <summary>
/// Schedule configuration for the Hangfire recurring sync-pipeline jobs, bound to the
/// <c>ScheduledJobs</c> section. The imports and the nightly reconcile are staggered by cron so
/// the ordered pass — Shopify product import, then full reconcile — runs without overlapping;
/// the two dispatchers run on short intervals and drain whatever is pending. A failing job never
/// blocks the others.
/// </summary>
public class ScheduledJobsOptions
{
    /// <summary>The configuration section key used to bind this options class.</summary>
    public const string SectionKey = "ScheduledJobs";

    [Required]
    public RecurringJobOptions ShopifyProductSync { get; init; } = new();

    [Required]
    public RecurringJobOptions SkulabsItemSync { get; init; } = new();

    [Required]
    public RecurringJobOptions FullReconcile { get; init; } = new();

    [Required]
    public RecurringJobOptions ShopifyDispatch { get; init; } = new();

    [Required]
    public RecurringJobOptions SkulabsDispatch { get; init; } = new();
}

/// <summary>
/// Scheduling for a single Hangfire recurring job. <see cref="Cron"/> is a standard 5-field cron
/// expression (Cronos syntax). A disabled job is removed from the schedule rather than registered.
/// </summary>
public class RecurringJobOptions
{
    /// <summary>Standard (Cronos) 5-field cron expression. Defaults to daily at midnight.</summary>
    public string Cron { get; init; } = "0 0 * * *";

    /// <summary>When <c>false</c>, the recurring job is removed from the schedule and never fires.</summary>
    public bool Enabled { get; init; } = true;
}
