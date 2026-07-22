using Microsoft.Extensions.Caching.Memory;

namespace Integration.RateLimiting;

/// <summary>
/// Process-local <see cref="IRateLimitService"/> backed by <see cref="IMemoryCache"/>. Entries
/// expire automatically once their cooldown elapses; nothing is persisted across process restarts.
/// </summary>
public class InMemoryRateLimitService : IRateLimitService
{
    /// <summary>Fallback cooldown applied when the upstream did not send a usable <c>Retry-After</c> header.</summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    private const string CacheKeyPrefix = "ratelimit:";

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;

    public InMemoryRateLimitService(IMemoryCache cache, TimeProvider timeProvider)
    {
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public TimeSpan? GetRemainingCooldown(string key)
    {
        if (!_cache.TryGetValue(BuildCacheKey(key), out var raw) || raw is not DateTimeOffset retryAt)
        {
            return null;
        }

        var remaining = retryAt - _timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public void RecordRateLimit(string key, TimeSpan? retryAfter)
    {
        var cooldown = retryAfter is { } supplied && supplied > TimeSpan.Zero
            ? supplied
            : DefaultCooldown;

        var retryAt = _timeProvider.GetUtcNow() + cooldown;
        _cache.Set(BuildCacheKey(key), retryAt, cooldown);
    }

    private static string BuildCacheKey(string key) => CacheKeyPrefix + key;
}
