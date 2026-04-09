using Application.Events;
using Application.Jobs;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using Shouldly;

namespace Tests.Application.Jobs;

public class ProductEventProcessorJobTests
{
    private readonly IProductEventAccumulator _eventAccumulator = Substitute.For<IProductEventAccumulator>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<ProductEventProcessorJob> _logger = new();

    public ProductEventProcessorJobTests()
    {
        // Default: no accumulated events.
        _eventAccumulator.DrainAll().Returns([]);
    }

    // -------------------------------------------------------------------------
    // Accumulator interaction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldDrainAccumulator_OnEveryRun()
    {
        await CreateSut().Execute(_context);

        _eventAccumulator.Received(1).DrainAll();
    }

    [Fact]
    public async Task Execute_ShouldProcessAllDrainedEvents_WhenEventsArePresent()
    {
        _eventAccumulator.DrainAll().Returns(
        [
            new ProductChangedEvent(100L, ProductChangeType.Created),
            new ProductChangedEvent(200L, ProductChangeType.Updated)
        ]);

        // Should complete without throwing.
        await CreateSut().Execute(_context);
    }

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldLogInformation_WhenJobStarts()
    {
        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("started"));
    }

    [Fact]
    public async Task Execute_ShouldLogInformation_WhenJobCompletes()
    {
        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public async Task Execute_ShouldLogDebug_WhenNoEventsAreAccumulated()
    {
        _eventAccumulator.DrainAll().Returns([]);

        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Debug && e.Message.Contains("No product change events"));
    }

    [Fact]
    public async Task Execute_ShouldLogEventCount_WhenEventsArePresent()
    {
        _eventAccumulator.DrainAll().Returns(
        [
            new ProductChangedEvent(100L, ProductChangeType.Created),
            new ProductChangedEvent(200L, ProductChangeType.Updated)
        ]);

        await CreateSut().Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("2"));
    }

    private ProductEventProcessorJob CreateSut() => new(_eventAccumulator, _logger);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
