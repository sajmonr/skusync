using Application.Skulabs.Maintenance;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Application.Skulabs;

public class SkuAndBarcodeSyncTaskTests
{
    private readonly ISkuAndBarcodeSyncService _syncService = Substitute.For<ISkuAndBarcodeSyncService>();

    [Fact]
    public async Task Execute_ShouldCallSyncAll()
    {
        _syncService.SyncAll(Arg.Any<CancellationToken>()).Returns(SkuAndBarcodeSyncResult.Empty);

        await CreateSut().Execute(CancellationToken.None);

        await _syncService.Received(1).SyncAll(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ShouldPropagateException_WhenSyncThrows()
    {
        var inner = new InvalidOperationException("boom");
        _syncService.SyncAll(Arg.Any<CancellationToken>()).ThrowsAsync(inner);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => CreateSut().Execute(CancellationToken.None));

        thrown.ShouldBeSameAs(inner);
    }

    [Fact]
    public async Task Execute_ShouldForwardCancellationToken_ToSyncService()
    {
        using var cts = new CancellationTokenSource();
        _syncService.SyncAll(Arg.Any<CancellationToken>()).Returns(SkuAndBarcodeSyncResult.Empty);

        await CreateSut().Execute(cts.Token);

        await _syncService.Received(1).SyncAll(cts.Token);
    }

    [Fact]
    public void Name_ShouldReturnClassName()
    {
        CreateSut().Name.ShouldBe(nameof(SkuAndBarcodeSyncTask));
    }

    private SkuAndBarcodeSyncTask CreateSut() =>
        new(_syncService, NullLogger<SkuAndBarcodeSyncTask>.Instance);
}
