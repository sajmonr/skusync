using Application;
using Application.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FeatureManagement;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests.Application.Sync;

public class ShopifyDispatchTriggerTests
{
    private readonly IShopifyDispatcher _dispatcher = Substitute.For<IShopifyDispatcher>();
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();

    public ShopifyDispatchTriggerTests()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyAutoDispatch).Returns(true);
        _dispatcher
            .DispatchVariants(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(DispatchResult.Empty);
    }

    [Fact]
    public async Task TryDispatch_ShouldDispatch_WhenAutoDispatchEnabled()
    {
        Guid[] ids = [Guid.NewGuid()];

        await CreateSut().TryDispatch(ids);

        await _dispatcher.Received(1).DispatchVariants(ids, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryDispatch_ShouldSkip_WhenAutoDispatchDisabled()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.ShopifyAutoDispatch).Returns(false);

        await CreateSut().TryDispatch([Guid.NewGuid()]);

        await _dispatcher.DidNotReceive()
            .DispatchVariants(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryDispatch_ShouldSkip_ForEmptyScope()
    {
        await CreateSut().TryDispatch([]);

        await _dispatcher.DidNotReceive()
            .DispatchVariants(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryDispatch_ShouldSwallowDispatchFailures_SoIngestNeverFails()
    {
        _dispatcher
            .DispatchVariants(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("shopify offline"));

        // Should not throw — the rows stay pending and the scheduled dispatch retries.
        await CreateSut().TryDispatch([Guid.NewGuid()]);
    }

    private ShopifyDispatchTrigger CreateSut() =>
        new(_dispatcher, _featureManager, NullLogger<ShopifyDispatchTrigger>.Instance);
}
