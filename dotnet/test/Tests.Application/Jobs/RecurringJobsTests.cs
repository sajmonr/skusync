using Application;
using Application.Jobs;
using Application.Products.Events;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using SlimMessageBus;

namespace Tests.Application.Jobs;

public class RecurringJobsTests
{
    private readonly ISkuAndBarcodeSyncService _skuAndBarcodeSyncService =
        Substitute.For<ISkuAndBarcodeSyncService>();
    private readonly ISkulabsTitleSyncService _skulabsTitleSyncService =
        Substitute.For<ISkulabsTitleSyncService>();
    private readonly ISkulabsItemSyncService _skulabsItemSyncService =
        Substitute.For<ISkulabsItemSyncService>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();

    public RecurringJobsTests()
    {
        // Default to enabled for the behavioural tests; the disabled path is asserted separately.
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsSyncEnabled).Returns(true);
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldCallSyncService()
    {
        _skulabsItemSyncService.Sync(Arg.Any<CancellationToken>()).Returns(SkulabsItemSyncResult.Empty);

        await CreateSut().SyncSkulabsItems();

        await _skulabsItemSyncService.Received(1).Sync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldPublishEventForEachCreatedAndUpdatedItem()
    {
        var created = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var updated = new[] { Guid.NewGuid() };
        _skulabsItemSyncService.Sync(Arg.Any<CancellationToken>()).Returns(
            new SkulabsItemSyncResult(created, updated, 0, 0, 0, 0, 0));

        await CreateSut().SyncSkulabsItems();

        foreach (var id in created.Concat(updated))
        {
            await _messageBus.Received(1).Publish(
                Arg.Is<SkulabsProductImportedEvent>(e => e.SkulabsProductId == id),
                Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldNotPublishEvents_WhenNothingChanged()
    {
        _skulabsItemSyncService.Sync(Arg.Any<CancellationToken>()).Returns(SkulabsItemSyncResult.Empty);

        await CreateSut().SyncSkulabsItems();

        await _messageBus.DidNotReceive().Publish(
            Arg.Any<SkulabsProductImportedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldPropagate_WhenSyncThrows()
    {
        var inner = new InvalidOperationException("boom");
        _skulabsItemSyncService.Sync(Arg.Any<CancellationToken>()).ThrowsAsync(inner);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateSut().SyncSkulabsItems());

        thrown.ShouldBeSameAs(inner);
        await _messageBus.DidNotReceive().Publish(
            Arg.Any<SkulabsProductImportedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncSkulabsItems_ShouldDoNothing_WhenSkulabsSyncFeatureFlagIsDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.SkulabsSyncEnabled).Returns(false);

        await CreateSut().SyncSkulabsItems();

        await _skulabsItemSyncService.DidNotReceive().Sync(Arg.Any<CancellationToken>());
        await _messageBus.DidNotReceive().Publish(
            Arg.Any<SkulabsProductImportedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    private RecurringJobs CreateSut() => new(
        _skuAndBarcodeSyncService,
        _skulabsTitleSyncService,
        _skulabsItemSyncService,
        _messageBus,
        _featureManager,
        NullLogger<RecurringJobs>.Instance);
}
