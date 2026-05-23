using Application.Products.Events;
using Application.Skulabs.Jobs;
using Application.Skulabs.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using Shouldly;
using SlimMessageBus;

namespace Tests.Application.Skulabs;

public class SkulabsItemSyncJobTests
{
    private readonly ISkulabsItemSyncService _syncService = Substitute.For<ISkulabsItemSyncService>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<SkulabsItemSyncJob> _logger = new();

    [Fact]
    public async Task Execute_ShouldCallSyncService()
    {
        _syncService.Sync(Arg.Any<CancellationToken>()).Returns(SkulabsItemSyncResult.Empty);
        var sut = CreateSut();

        await sut.Execute(_context);

        await _syncService.Received(1).Sync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ShouldPublishEventForEachCreatedAndUpdatedItem()
    {
        var created = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var updated = new[] { Guid.NewGuid() };
        _syncService.Sync(Arg.Any<CancellationToken>()).Returns(
            new SkulabsItemSyncResult(created, updated, 0, 0));

        var sut = CreateSut();
        await sut.Execute(_context);

        foreach (var id in created.Concat(updated))
        {
            await _messageBus.Received(1).Publish(
                Arg.Is<SkulabsProductImportedEvent>(e => e.SkulabsProductId == id),
                Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Execute_ShouldNotPublishEvents_WhenNothingChanged()
    {
        _syncService.Sync(Arg.Any<CancellationToken>()).Returns(SkulabsItemSyncResult.Empty);
        var sut = CreateSut();

        await sut.Execute(_context);

        await _messageBus.DidNotReceive().Publish(
            Arg.Any<SkulabsProductImportedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ShouldThrowJobExecutionException_WhenSyncThrows()
    {
        var inner = new InvalidOperationException("boom");
        _syncService.Sync(Arg.Any<CancellationToken>()).ThrowsAsync(inner);
        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        thrown.InnerException.ShouldBeSameAs(inner);
        thrown.RefireImmediately.ShouldBeFalse();
    }

    [Fact]
    public async Task Execute_ShouldNotPublishEvents_WhenSyncThrows()
    {
        _syncService.Sync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        await _messageBus.DidNotReceive().Publish(
            Arg.Any<SkulabsProductImportedEvent>(),
            Arg.Any<string?>(), Arg.Any<IDictionary<string, object>?>(), Arg.Any<CancellationToken>());
    }

    private SkulabsItemSyncJob CreateSut() => new(_syncService, _messageBus, _logger);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
