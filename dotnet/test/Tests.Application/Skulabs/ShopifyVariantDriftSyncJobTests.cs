using Application.Skulabs.Jobs;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using Shouldly;

namespace Tests.Application.Skulabs;

public class ShopifyVariantDriftSyncJobTests
{
    private readonly IShopifyVariantDriftSyncService _driftService = Substitute.For<IShopifyVariantDriftSyncService>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<ShopifyVariantDriftSyncJob> _logger = new();

    [Fact]
    public async Task Execute_ShouldCallSyncAll()
    {
        _driftService.SyncAll(Arg.Any<CancellationToken>()).Returns(ShopifyVariantDriftSyncResult.Empty);

        await CreateSut().Execute(_context);

        await _driftService.Received(1).SyncAll(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ShouldThrowJobExecutionException_WhenSyncThrows()
    {
        var inner = new InvalidOperationException("boom");
        _driftService.SyncAll(Arg.Any<CancellationToken>()).ThrowsAsync(inner);

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => CreateSut().Execute(_context));

        thrown.InnerException.ShouldBeSameAs(inner);
        thrown.RefireImmediately.ShouldBeFalse();
    }

    private ShopifyVariantDriftSyncJob CreateSut() => new(_driftService, _logger);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
