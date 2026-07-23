using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Infrastructure.Database;

internal sealed class PostgresMigrationLock : IAsyncDisposable
{
    private const long MigrationLockId = 0x536B7553796E634D;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly NpgsqlConnection _connection;
    private readonly ILogger _logger;
    private bool _isReleased;

    private PostgresMigrationLock(NpgsqlConnection connection, ILogger logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public static async Task<PostgresMigrationLock> Acquire(
        string connectionString,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Waiting up to {LockTimeoutMs}ms for database migration lock {LockId}.",
            timeout.TotalMilliseconds,
            MigrationLockId);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(linkedCancellation.Token);
            while (!await TryAcquire(connection, linkedCancellation.Token))
            {
                await Task.Delay(PollInterval, linkedCancellation.Token);
            }

            logger.LogInformation(
                "Acquired database migration lock {LockId} after {ElapsedMs}ms.",
                MigrationLockId,
                stopwatch.ElapsedMilliseconds);
            return new PostgresMigrationLock(connection, logger);
        }
        catch (OperationCanceledException exception) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync();
            logger.LogError(
                exception,
                "Timed out waiting {ElapsedMs}ms for database migration lock {LockId}.",
                stopwatch.ElapsedMilliseconds,
                MigrationLockId);
            throw new DatabaseMigrationLockTimeoutException(timeout);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            logger.LogError(
                exception,
                "Failed to acquire database migration lock {LockId} after {ElapsedMs}ms.",
                MigrationLockId,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isReleased)
        {
            return;
        }

        _isReleased = true;
        try
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                await using var command = CreateLockCommand(
                    "SELECT pg_advisory_unlock(@lockId)",
                    _connection);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Explicit release of database migration lock {LockId} failed; closing the connection will release it.",
                MigrationLockId);
        }
        finally
        {
            await _connection.DisposeAsync();
            _logger.LogInformation("Released database migration lock {LockId}.", MigrationLockId);
        }
    }

    private static async Task<bool> TryAcquire(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = CreateLockCommand(
            "SELECT pg_try_advisory_lock(@lockId)",
            connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static NpgsqlCommand CreateLockCommand(string commandText, NpgsqlConnection connection)
    {
        var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddWithValue("lockId", NpgsqlDbType.Bigint, MigrationLockId);
        return command;
    }
}
