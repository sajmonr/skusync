using System.ComponentModel.DataAnnotations;

namespace Application.Sync;

/// <summary>
/// Pacing for <see cref="SyncDrainLoop"/>.
/// <para>
/// The two targets are configured separately because only one of them is scarce. Shopify accepts
/// webhooks inbound and a generous write budget outbound; SkuLabs allows 104 requests an hour
/// <em>per account</em>, shared between our polling, our pushes, and every other consumer of that
/// account — including their own UI. Measured against a live account, roughly 16 of those were
/// already spent before we asked for anything.
/// </para>
/// </summary>
public class SyncDrainLoopOptions
{
    public const string SectionKey = "SyncDrainLoop";

    /// <summary>
    /// Whether the loop runs at all. Off leaves the scheduled dispatch jobs as the only drain,
    /// which still converges — just on their cadence.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How often to look for work. Cheap: an indexed query against rows already in Postgres, with
    /// no outbound call unless something is actually pending.
    /// </summary>
    [Range(1, 600)]
    public int TickSeconds { get; init; } = 10;

    /// <summary>
    /// Shortest gap between two SkuLabs pushes, whatever the tick finds.
    /// <para>
    /// This is the whole rate-limit strategy, and it is deliberately an interval rather than a
    /// budget. A local counter cannot work here: the quota is per-account and spent by consumers we
    /// neither see nor instrument, and SkuLabs returns no rate-limit headers on success, so nothing
    /// would ever correct the counter's drift.
    /// </para>
    /// <para>
    /// An interval sidesteps that by capping our own contribution outright — at 45s, at most 80
    /// requests an hour, which sits inside the measured limit even alongside the observed baseline.
    /// It costs nothing in latency for the common case, because after any quiet spell the interval
    /// has already elapsed and the next tick pushes immediately. Only sustained churn is paced, and
    /// pacing churn is free: a bulk upsert carries the whole pending set in one request, so waiting
    /// longer means a bigger batch rather than more requests.
    /// </para>
    /// </summary>
    [Range(0, 3600)]
    public int SkulabsMinimumPushIntervalSeconds { get; init; } = 45;
}
