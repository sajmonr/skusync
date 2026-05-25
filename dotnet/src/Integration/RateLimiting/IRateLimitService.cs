namespace Integration.RateLimiting;

/// <summary>
/// Process-wide tracker of cooldown windows for external services that have rate-limited us.
/// Lets one in-flight call's 429 short-circuit later calls for the same client so they don't
/// burn additional 429s discovering the limit independently.
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Returns the remaining cooldown for the given key, or <c>null</c> when no active
    /// rate limit is recorded.
    /// </summary>
    /// <param name="key">A stable identifier for the rate-limited resource — typically a client name (e.g. <c>"skulabs"</c>).</param>
    TimeSpan? GetRemainingCooldown(string key);

    /// <summary>
    /// Records that <paramref name="key"/> is rate-limited for the supplied duration. When
    /// <paramref name="retryAfter"/> is <c>null</c> a default cooldown of 5 minutes is used.
    /// Subsequent calls to <see cref="GetRemainingCooldown(string)"/> within the window
    /// return the remaining time. Calling again before expiry replaces the existing entry.
    /// </summary>
    void RecordRateLimit(string key, TimeSpan? retryAfter);
}
