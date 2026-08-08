using Application;
using Application.Jobs;
using Application.Skulabs.Services;
using Application.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FeatureManagement;
using NSubstitute;
using Shouldly;

namespace Tests.Application.Sync;

/// <summary>
/// The loop's only real responsibility is pacing SkuLabs. Everything else about it is best-effort:
/// the pending flags are durable and the scheduled jobs still sweep, so a missed tick costs latency
/// rather than correctness. The interval is not best-effort — exceeding it spends a quota shared
/// with consumers we cannot see.
/// </summary>
public class SyncDrainLoopTests
{
    private readonly IShopifyDispatcher _shopifyDispatcher = Substitute.For<IShopifyDispatcher>();
    private readonly ISkulabsDispatcher _skulabsDispatcher = Substitute.For<ISkulabsDispatcher>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();
    private readonly FakeTimeProvider _time = new();

    public SyncDrainLoopTests()
    {
        _featureManager.IsEnabledAsync(Arg.Any<string>()).Returns(true);
        _shopifyDispatcher.DispatchAll(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new DispatchResult(Pending: 1, Pushed: 1, Failed: 0)));
        _skulabsDispatcher.DispatchAll(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new DispatchResult(Pending: 1, Pushed: 1, Failed: 0)));
    }

    [Fact]
    public async Task ShouldDrainShopify_OnEveryTick()
    {
        using var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        await AdvanceTicks(3);

        await _shopifyDispatcher.Received(3).DispatchAll(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Shopify has room; SkuLabs does not. Ticking them at the same rate would spend the whole
    /// hourly allowance in minutes.
    /// </summary>
    [Fact]
    public async Task ShouldNotDrainSkulabs_MoreOftenThanTheMinimumInterval()
    {
        using var sut = CreateSut(tickSeconds: 10, skulabsIntervalSeconds: 45);
        await sut.StartAsync(CancellationToken.None);

        await AdvanceTicks(4); // 40 seconds — inside the interval after the first push

        await _skulabsDispatcher.Received(1).DispatchAll(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldDrainSkulabsAgain_OnceTheIntervalHasElapsed()
    {
        using var sut = CreateSut(tickSeconds: 10, skulabsIntervalSeconds: 45);
        await sut.StartAsync(CancellationToken.None);

        await AdvanceTicks(6); // 60 seconds — past the interval

        await _skulabsDispatcher.Received(2).DispatchAll(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A rate-limited run has still spent a request, so it restarts the clock. Timing from success
    /// instead would retry a throttled target on every tick — the case where restraint matters most.
    /// </summary>
    [Fact]
    public async Task ShouldPaceFromTheAttempt_NotFromASuccessfulPush()
    {
        _skulabsDispatcher.DispatchAll(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new DispatchResult(
                Pending: 1, Pushed: 0, Failed: 0, RetryAfter: TimeSpan.FromMinutes(25))));

        using var sut = CreateSut(tickSeconds: 10, skulabsIntervalSeconds: 45);
        await sut.StartAsync(CancellationToken.None);

        await AdvanceTicks(4);

        await _skulabsDispatcher.Received(1).DispatchAll(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// With nothing pending the clock does not start, so a change arriving after a quiet spell goes
    /// out on the very next tick instead of waiting out an interval it never used.
    /// </summary>
    [Fact]
    public async Task ShouldNotStartTheInterval_WhenThereWasNothingToPush()
    {
        _skulabsDispatcher.DispatchAll(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(DispatchResult.Empty));

        using var sut = CreateSut(tickSeconds: 10, skulabsIntervalSeconds: 45);
        await sut.StartAsync(CancellationToken.None);

        await AdvanceTicks(3);

        await _skulabsDispatcher.Received(3).DispatchAll(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldDoNothing_WhenDisabled()
    {
        using var sut = CreateSut(enabled: false);
        await sut.StartAsync(CancellationToken.None);

        await AdvanceTicks(3);

        await _shopifyDispatcher.DidNotReceive().DispatchAll(Arg.Any<CancellationToken>());
    }

    /// <summary>One failing tick must not silently end the loop for the life of the process.</summary>
    [Fact]
    public async Task ShouldKeepTicking_AfterADrainThrows()
    {
        _shopifyDispatcher.DispatchAll(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<DispatchResult>(new InvalidOperationException("boom")),
                _ => Task.FromResult(new DispatchResult(Pending: 1, Pushed: 1, Failed: 0)));

        using var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);

        await AdvanceTicks(3);

        await _shopifyDispatcher.Received(3).DispatchAll(Arg.Any<CancellationToken>());
    }

    private async Task AdvanceTicks(int count)
    {
        // Let the loop reach its first wait before moving the clock; a tick raised before it is
        // listening is simply not observed, and the test would read that as "never ran".
        await Task.Delay(50);

        for (var i = 0; i < count; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            // The loop awaits the dispatchers, so yield until it has come back round to the timer.
            await Task.Delay(50);
        }
    }

    private SyncDrainLoop CreateSut(
        bool enabled = true,
        int tickSeconds = 10,
        int skulabsIntervalSeconds = 45)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_shopifyDispatcher);
        services.AddSingleton(_skulabsDispatcher);
        services.AddSingleton(_featureManager);
        services.AddSingleton(Substitute.For<ISkulabsItemSyncService>());
        services.AddSingleton(Substitute.For<IReconciler>());
        services.AddSingleton<ILogger<RecurringJobs>>(NullLogger<RecurringJobs>.Instance);
        services.AddTransient<RecurringJobs>();

        return new SyncDrainLoop(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SyncDrainLoopOptions
            {
                Enabled = enabled,
                TickSeconds = tickSeconds,
                SkulabsMinimumPushIntervalSeconds = skulabsIntervalSeconds
            }),
            _time,
            NullLogger<SyncDrainLoop>.Instance);
    }
}
