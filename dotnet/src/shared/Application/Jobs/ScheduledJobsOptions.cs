using System.ComponentModel.DataAnnotations;

namespace Application.Jobs;

/// <summary>
/// Schedule configuration for the Hangfire recurring maintenance jobs, bound to the
/// <c>ScheduledJobs</c> section. Each job is independent and staggered by cron so the ordered
/// sweep — Shopify product sync, then SKU/barcode, then SkuLabs title — runs without overlapping,
/// and a failing job never blocks the others.
/// </summary>
public class ScheduledJobsOptions
{
    /// <summary>The configuration section key used to bind this options class.</summary>
    public const string SectionKey = "ScheduledJobs";

    [Required]
    public RecurringJobOptions ShopifyProductSync { get; init; } = new();

    [Required]
    public RecurringJobOptions SkuAndBarcodeSync { get; init; } = new();

    [Required]
    public RecurringJobOptions SkulabsTitleSync { get; init; } = new();

    [Required]
    public RecurringJobOptions SkulabsItemSync { get; init; } = new();
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
