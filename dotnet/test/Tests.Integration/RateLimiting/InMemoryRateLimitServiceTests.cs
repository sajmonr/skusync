using Integration.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Shouldly;

namespace Tests.Integration.RateLimiting;

public class InMemoryRateLimitServiceTests
{
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public void GetRemainingCooldown_ReturnsNull_WhenNoEntryRecorded()
    {
        var sut = CreateSut();

        sut.GetRemainingCooldown("skulabs").ShouldBeNull();
    }

    [Fact]
    public void RecordRateLimit_StoresExplicitRetryAfter_WhenSupplied()
    {
        var sut = CreateSut();

        sut.RecordRateLimit("skulabs", TimeSpan.FromSeconds(45));

        sut.GetRemainingCooldown("skulabs").ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void RecordRateLimit_UsesDefaultCooldown_WhenRetryAfterIsNull()
    {
        var sut = CreateSut();

        sut.RecordRateLimit("skulabs", retryAfter: null);

        sut.GetRemainingCooldown("skulabs").ShouldBe(InMemoryRateLimitService.DefaultCooldown);
        InMemoryRateLimitService.DefaultCooldown.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void RecordRateLimit_UsesDefaultCooldown_WhenRetryAfterIsNonPositive()
    {
        var sut = CreateSut();

        sut.RecordRateLimit("skulabs", TimeSpan.Zero);

        sut.GetRemainingCooldown("skulabs").ShouldBe(InMemoryRateLimitService.DefaultCooldown);
    }

    [Fact]
    public void GetRemainingCooldown_DecaysOverTime()
    {
        var sut = CreateSut();
        sut.RecordRateLimit("skulabs", TimeSpan.FromSeconds(60));

        _time.Advance(TimeSpan.FromSeconds(25));

        sut.GetRemainingCooldown("skulabs").ShouldBe(TimeSpan.FromSeconds(35));
    }

    [Fact]
    public void GetRemainingCooldown_ReturnsNull_AfterCooldownExpires()
    {
        var sut = CreateSut();
        sut.RecordRateLimit("skulabs", TimeSpan.FromSeconds(10));

        _time.Advance(TimeSpan.FromSeconds(11));

        sut.GetRemainingCooldown("skulabs").ShouldBeNull();
    }

    [Fact]
    public void RecordRateLimit_IsScopedByKey()
    {
        var sut = CreateSut();

        sut.RecordRateLimit("skulabs", TimeSpan.FromSeconds(60));

        sut.GetRemainingCooldown("shopify").ShouldBeNull();
        sut.GetRemainingCooldown("skulabs").ShouldNotBeNull();
    }

    [Fact]
    public void RecordRateLimit_OverwritesExistingEntry()
    {
        var sut = CreateSut();
        sut.RecordRateLimit("skulabs", TimeSpan.FromSeconds(10));

        sut.RecordRateLimit("skulabs", TimeSpan.FromMinutes(2));

        sut.GetRemainingCooldown("skulabs").ShouldBe(TimeSpan.FromMinutes(2));
    }

    private InMemoryRateLimitService CreateSut() => new(_cache, _time);

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
