using Application.Jobs.Maintenance;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using Shouldly;

namespace Tests.Application.Jobs.Maintenance;

public class ProductMaintenanceJobTests
{
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<ProductMaintenanceJob> _logger = new();

    public ProductMaintenanceJobTests()
    {
        _context.Trigger.Returns(Substitute.For<ITrigger>());
        _context.CancellationToken.Returns(CancellationToken.None);
    }

    [Fact]
    public async Task Execute_ShouldRunAllTasks_InOrder()
    {
        var order = new List<string>();
        var first = new RecordingTask("first", order);
        var second = new RecordingTask("second", order);

        await CreateSut(first, second).Execute(_context);

        order.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task Execute_ShouldContinueWithRemainingTasks_WhenOneFails()
    {
        var order = new List<string>();
        var first = Substitute.For<IMaintenanceTask>();
        first.Name.Returns("first");
        first.Execute(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        var second = new RecordingTask("second", order);

        await CreateSut(first, second).Execute(_context);

        order.ShouldBe(["second"]);
    }

    [Fact]
    public async Task Execute_ShouldLogError_WhenTaskFails()
    {
        var failing = Substitute.For<IMaintenanceTask>();
        failing.Name.Returns("failing");
        var exception = new InvalidOperationException("boom");
        failing.Execute(Arg.Any<CancellationToken>()).ThrowsAsync(exception);

        await CreateSut(failing).Execute(_context);

        var errors = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errors.Length.ShouldBe(1);
        errors[0].Exception.ShouldBeSameAs(exception);
        errors[0].Message.ShouldContain("failing");
    }

    [Fact]
    public async Task Execute_ShouldNotThrow_WhenTaskFails()
    {
        var failing = Substitute.For<IMaintenanceTask>();
        failing.Name.Returns("failing");
        failing.Execute(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        await Should.NotThrowAsync(() => CreateSut(failing).Execute(_context));
    }

    [Fact]
    public async Task Execute_ShouldLogSummary_WithSucceededAndFailedCounts()
    {
        var passing = new RecordingTask("ok", []);
        var failing = Substitute.For<IMaintenanceTask>();
        failing.Name.Returns("bad");
        failing.Execute(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        await CreateSut(passing, failing).Execute(_context);

        _logger.Entries.ShouldContain(e =>
            e.LogLevel == LogLevel.Information &&
            e.Message.Contains("Succeeded") &&
            e.Message.Contains("Failed"));
    }

    [Fact]
    public async Task Execute_ShouldForwardCancellationToken_ToTasks()
    {
        using var cts = new CancellationTokenSource();
        _context.CancellationToken.Returns(cts.Token);
        var task = Substitute.For<IMaintenanceTask>();
        task.Name.Returns("t");

        await CreateSut(task).Execute(_context);

        await task.Received(1).Execute(cts.Token);
    }

    [Fact]
    public async Task Execute_ShouldOrderTasks_ByTaskOrderAttribute_RegardlessOfRegistration()
    {
        // Register in reverse-attribute order; the orchestrator should still run them
        // in attribute order (1, 2, 3).
        var order = new List<string>();
        var third = new RecordingTaskWithOrder3("third", order);
        var first = new RecordingTaskWithOrder1("first", order);
        var second = new RecordingTaskWithOrder2("second", order);

        await CreateSut(third, first, second).Execute(_context);

        order.ShouldBe(["first", "second", "third"]);
    }

    [Fact]
    public async Task Execute_ShouldRunUnorderedTasks_AfterOrderedTasks_InRegistrationOrder()
    {
        var order = new List<string>();
        var ordered = new RecordingTaskWithOrder1("ordered", order);
        var unorderedA = new RecordingTask("unordered-a", order);
        var unorderedB = new RecordingTask("unordered-b", order);

        await CreateSut(unorderedA, ordered, unorderedB).Execute(_context);

        order.ShouldBe(["ordered", "unordered-a", "unordered-b"]);
    }

    private ProductMaintenanceJob CreateSut(params IMaintenanceTask[] tasks) =>
        new(tasks, _logger);

    private sealed class RecordingTask(string name, List<string> order) : IMaintenanceTask
    {
        public string Name => name;

        public Task Execute(CancellationToken cancellationToken)
        {
            order.Add(name);
            return Task.CompletedTask;
        }
    }

    [TaskOrder(1)]
    private sealed class RecordingTaskWithOrder1(string name, List<string> order) : IMaintenanceTask
    {
        public string Name => name;

        public Task Execute(CancellationToken cancellationToken)
        {
            order.Add(name);
            return Task.CompletedTask;
        }
    }

    [TaskOrder(2)]
    private sealed class RecordingTaskWithOrder2(string name, List<string> order) : IMaintenanceTask
    {
        public string Name => name;

        public Task Execute(CancellationToken cancellationToken)
        {
            order.Add(name);
            return Task.CompletedTask;
        }
    }

    [TaskOrder(3)]
    private sealed class RecordingTaskWithOrder3(string name, List<string> order) : IMaintenanceTask
    {
        public string Name => name;

        public Task Execute(CancellationToken cancellationToken)
        {
            order.Add(name);
            return Task.CompletedTask;
        }
    }

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
