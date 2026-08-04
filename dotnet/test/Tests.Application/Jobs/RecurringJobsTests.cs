using Application;
using Application.Jobs;
using Application.Skulabs.Services;
using Application.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Jobs;

public class RecurringJobsTests
{
    private readonly ISkulabsItemSyncService _skulabsItemSyncService =
        Substitute.For<ISkulabsItemSyncService>();
    private readonly IReconciler _reconciler = Substitute.For<IReconciler>();
    private readonly IShopifyDispatcher _shopifyDispatcher = Substitute.For<IShopifyDispatcher>();
    private readonly ISkulabsDispatcher _skulabsDispatcher = Substitute.For<ISkulabsDispatcher>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();

    public RecurringJobsTests()
    {
        // Default to enabled for the behavioural tests; disabled paths are asserted separately.
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsSyncEnabled).Returns(true);
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyAutoDispatch).Returns(true);
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsAutoDispatch).Returns(true);

        _skulabsItemSyncService.Sync(Arg.Any<CancellationToken>()).Returns(SkulabsItemSyncResult.Empty);
        _reconciler.ReconcileAll(Arg.Any<CancellationToken>()).Returns(ReconcileResult.Empty);
        _reconciler.ReconcileSkulabsItems(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ReconcileResult.Empty);
        _shopifyDispatcher.DispatchAll(Arg.Any<CancellationToken>()).Returns(DispatchResult.Empty);
        _skulabsDispatcher.DispatchAll(Arg.Any<CancellationToken>()).Returns(DispatchResult.Empty);
    }

    // ---------- SyncSkulabsItems ----------

    [Fact]
    public async Task SyncSkulabsItems_ShouldCallSyncService()
    {
        await CreateSut().SyncSkulabsItems();

        await _skulabsItemSyncService.Received(1).Sync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldReconcileEveryCreatedAndRelinkedItem()
    {
        var created = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var updated = new[] { Guid.NewGuid() };
        _skulabsItemSyncService.Sync(Arg.Any<CancellationToken>()).Returns(
            new SkulabsItemSyncResult(created, updated, 0, 0, 0, 0, 0));

        await CreateSut().SyncSkulabsItems();

        await _reconciler.Received(1).ReconcileSkulabsItems(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 3 && created.Concat(updated).All(ids.Contains)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldPropagate_WhenSyncThrows()
    {
        var inner = new InvalidOperationException("boom");
        _skulabsItemSyncService.Sync(Arg.Any<CancellationToken>()).ThrowsAsync(inner);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateSut().SyncSkulabsItems());

        thrown.ShouldBeSameAs(inner);
        await _reconciler.DidNotReceive().ReconcileSkulabsItems(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldDoNothing_WhenSkulabsSyncFeatureFlagIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsSyncEnabled).Returns(false);

        await CreateSut().SyncSkulabsItems();

        await _skulabsItemSyncService.DidNotReceive().Sync(Arg.Any<CancellationToken>());
    }

    // ---------- ReconcileAll ----------

    [Fact]
    public async Task ReconcileAll_ShouldRunTheReconciler_Unconditionally()
    {
        await CreateSut().ReconcileAll();

        await _reconciler.Received(1).ReconcileAll(Arg.Any<CancellationToken>());
    }

    // ---------- Dispatch jobs (gated by the auto flags) ----------

    [Fact]
    public async Task DispatchShopify_ShouldDrain_WhenAutoDispatchEnabled()
    {
        await CreateSut().DispatchShopify();

        await _shopifyDispatcher.Received(1).DispatchAll(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchShopify_ShouldDoNothing_WhenAutoDispatchDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyAutoDispatch).Returns(false);

        await CreateSut().DispatchShopify();

        await _shopifyDispatcher.DidNotReceive().DispatchAll(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchSkulabs_ShouldDrain_WhenAutoDispatchEnabled()
    {
        await CreateSut().DispatchSkulabs();

        await _skulabsDispatcher.Received(1).DispatchAll(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchSkulabs_ShouldDoNothing_WhenAutoDispatchDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsAutoDispatch).Returns(false);

        await CreateSut().DispatchSkulabs();

        await _skulabsDispatcher.DidNotReceive().DispatchAll(Arg.Any<CancellationToken>());
    }

    private RecurringJobs CreateSut() => new(
        _skulabsItemSyncService,
        _reconciler,
        _shopifyDispatcher,
        _skulabsDispatcher,
        _featureManager,
        NullLogger<RecurringJobs>.Instance);
}
