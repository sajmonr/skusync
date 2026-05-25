namespace Integration.RateLimiting;

/// <summary>
/// Thrown when a request is short-circuited because the target service is in a known
/// rate-limited cooldown. Callers can treat it like any other transient HTTP failure —
/// the cooldown is honoured per <see cref="IRateLimitService"/>.
/// </summary>
public sealed class RateLimitedException : Exception
{
    /// <summary>The rate-limit key (typically the client name) that is in cooldown.</summary>
    public string Key { get; }

    /// <summary>Remaining cooldown at the time the exception was raised.</summary>
    public TimeSpan RetryAfter { get; }

    public RateLimitedException(string key, TimeSpan retryAfter)
        : base($"'{key}' is rate-limited. Retry after {retryAfter}.")
    {
        Key = key;
        RetryAfter = retryAfter;
    }
}
