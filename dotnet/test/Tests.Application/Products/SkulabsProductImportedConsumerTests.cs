using Application.Products.Events;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests.Application.Products;

public class SkulabsProductImportedConsumerTests
{
    private readonly IShopifyVariantDriftSyncService _driftService = Substitute.For<IShopifyVariantDriftSyncService>();
    private readonly TestLogger<SkulabsProductImportedConsumer> _logger = new();

    [Fact]
    public async Task OnHandle_ShouldCallSyncForSkulabsItem()
    {
        var id = Guid.NewGuid();

        await CreateSut().OnHandle(new SkulabsProductImportedEvent(id), CancellationToken.None);

        await _driftService.Received(1).SyncForSkulabsItem(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnHandle_ShouldSwallowExceptions_SoBatchSiblingsKeepProcessing()
    {
        _driftService
            .SyncForSkulabsItem(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("shopify offline"));

        // Should not throw.
        await CreateSut().OnHandle(new SkulabsProductImportedEvent(Guid.NewGuid()), CancellationToken.None);
    }

    private SkulabsProductImportedConsumer CreateSut() => new(_driftService, _logger);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
