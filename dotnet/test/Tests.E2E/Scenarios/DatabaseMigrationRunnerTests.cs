using System.Collections.Concurrent;
using Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Tests.E2E.Infrastructure;

namespace Tests.E2E.Scenarios;

[Collection(E2ETestCollection.Name)]
public class DatabaseMigrationRunnerTests(AppServerTestHost factory)
{
    [Fact]
    public async Task Run_ShouldSerializeConcurrentMigrationAttempts()
    {
        var releaseFirstMigration = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMigrationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondMigrationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new TestLogger<DatabaseMigrationRunner>();
        var firstMigrator = new TestDatabaseMigrator(
            factory.PostgreSqlConnectionString,
            async _ =>
            {
                firstMigrationStarted.SetResult();
                await releaseFirstMigration.Task;
            });
        var secondMigrator = new TestDatabaseMigrator(
            factory.PostgreSqlConnectionString,
            _ =>
            {
                secondMigrationStarted.SetResult();
                return Task.CompletedTask;
            });

        var firstAttempt = CreateRunner(firstMigrator, logger).Run();
        await firstMigrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondAttempt = CreateRunner(secondMigrator, logger).Run();
        await WaitUntilBothAttemptsAreWaiting(logger);

        secondMigrationStarted.Task.IsCompleted.ShouldBeFalse();
        releaseFirstMigration.SetResult();

        await Task.WhenAll(firstAttempt, secondAttempt).WaitAsync(TimeSpan.FromSeconds(5));
        firstMigrator.PendingMigrationChecks.ShouldBe(1);
        secondMigrator.PendingMigrationChecks.ShouldBe(1);
        firstMigrator.MigrationCalls.ShouldBe(1);
        secondMigrator.MigrationCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Run_ShouldLetSecondHostMigrate_WhenFirstHostMigrationFails()
    {
        var failFirstMigration = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMigrationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondMigrationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new TestLogger<DatabaseMigrationRunner>();
        var firstMigrator = new TestDatabaseMigrator(
            factory.PostgreSqlConnectionString,
            async _ =>
            {
                firstMigrationStarted.SetResult();
                await failFirstMigration.Task;
                throw new InvalidOperationException("transient migration failure");
            });
        var secondMigrator = new TestDatabaseMigrator(
            factory.PostgreSqlConnectionString,
            _ =>
            {
                secondMigrationStarted.SetResult();
                return Task.CompletedTask;
            });

        var firstAttempt = CreateRunner(firstMigrator, logger).Run();
        await firstMigrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondAttempt = CreateRunner(secondMigrator, logger).Run();
        await WaitUntilBothAttemptsAreWaiting(logger);
        failFirstMigration.SetResult();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => firstAttempt);
        thrown.Message.ShouldBe("transient migration failure");
        await secondAttempt.WaitAsync(TimeSpan.FromSeconds(5));

        secondMigrationStarted.Task.IsCompleted.ShouldBeTrue();
        firstMigrator.MigrationCalls.ShouldBe(1);
        secondMigrator.MigrationCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Run_ShouldTimeOutWithoutInvokingMigration_WhenLockIsNotReleased()
    {
        var logger = new TestLogger<DatabaseMigrationRunner>();
        await using var heldLock = await PostgresMigrationLock.Acquire(
            factory.PostgreSqlConnectionString,
            TimeSpan.FromSeconds(5),
            logger,
            CancellationToken.None);
        var migrator = new TestDatabaseMigrator(
            factory.PostgreSqlConnectionString,
            _ => Task.CompletedTask);
        var runner = new DatabaseMigrationRunner(
            migrator,
            Options.Create(new DatabaseMigrationOptions { LockTimeoutSeconds = 1 }),
            logger);

        await Should.ThrowAsync<DatabaseMigrationLockTimeoutException>(() => runner.Run());

        migrator.PendingMigrationChecks.ShouldBe(0);
        migrator.MigrationCalls.ShouldBe(0);
        logger.Messages.ShouldContain(message =>
            message.StartsWith("Timed out waiting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_ShouldReleaseLock_WhenMigrationIsCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        var migrationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new TestLogger<DatabaseMigrationRunner>();
        var canceledMigrator = new TestDatabaseMigrator(
            factory.PostgreSqlConnectionString,
            async cancellationToken =>
            {
                migrationStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        var recoveringMigrator = new TestDatabaseMigrator(
            factory.PostgreSqlConnectionString,
            _ => Task.CompletedTask);

        var canceledAttempt = CreateRunner(canceledMigrator, logger).Run(cancellation.Token);
        await migrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => canceledAttempt);
        await CreateRunner(recoveringMigrator, logger)
            .Run()
            .WaitAsync(TimeSpan.FromSeconds(5));

        recoveringMigrator.PendingMigrationChecks.ShouldBe(1);
        recoveringMigrator.MigrationCalls.ShouldBe(1);
    }

    private static DatabaseMigrationRunner CreateRunner(
        IDatabaseMigrator migrator,
        ILogger<DatabaseMigrationRunner> logger) =>
        new(
            migrator,
            Options.Create(new DatabaseMigrationOptions { LockTimeoutSeconds = 5 }),
            logger);

    private static Task WaitUntilBothAttemptsAreWaiting(
        TestLogger<DatabaseMigrationRunner> logger) =>
        AsyncWait.UntilAsync(
            () => logger.Messages.Count(message =>
                message.StartsWith("Waiting up to", StringComparison.Ordinal)) == 2,
            because: "Both migration runners should have begun lock acquisition.");

    private sealed class TestDatabaseMigrator(
        string connectionString,
        Func<CancellationToken, Task> migrate) : IDatabaseMigrator
    {
        private int _pendingMigrationChecks;
        private int _migrationCalls;

        public string ConnectionString { get; } = connectionString;

        public int MigrationCalls => _migrationCalls;

        public int PendingMigrationChecks => _pendingMigrationChecks;

        public Task<string[]> GetPendingMigrations(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _pendingMigrationChecks);
            return Task.FromResult(Array.Empty<string>());
        }

        public async Task Migrate(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _migrationCalls);
            await migrate(cancellationToken);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue(formatter(state, exception));
    }
}
