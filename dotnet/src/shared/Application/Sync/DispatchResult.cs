namespace Application.Sync;

/// <summary>
/// Outcome of a single dispatcher run — the drain of pending (dirty) rows toward one external
/// system.
/// </summary>
/// <param name="Pending">Number of dirty rows the run considered.</param>
/// <param name="Pushed">Number successfully written to the target and cleared.</param>
/// <param name="Failed">Number whose write failed; they stay pending and are retried on the next run. Zero when the run was skipped by the kill switch or rate limiting — a deliberate skip is not a failure.</param>
/// <param name="RetryAfter">Set when the target reported a rate-limit cooldown; the remaining cooldown at the time of the run.</param>
public readonly record struct DispatchResult(
    int Pending,
    int Pushed,
    int Failed,
    TimeSpan? RetryAfter = null)
{
    public static DispatchResult Empty => new(0, 0, 0);

    /// <summary>True when the run was cut short by the target's rate limiting.</summary>
    public bool RateLimited => RetryAfter is not null;
}
