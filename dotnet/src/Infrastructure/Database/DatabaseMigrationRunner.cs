using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Database;

internal sealed class DatabaseMigrationRunner(
    IDatabaseMigrator databaseMigrator,
    IOptions<DatabaseMigrationOptions> options,
    ILogger<DatabaseMigrationRunner> logger)
{
    public async Task Run(CancellationToken cancellationToken = default)
    {
        var lockTimeout = TimeSpan.FromSeconds(options.Value.LockTimeoutSeconds);
        await using var migrationLock = await PostgresMigrationLock.Acquire(
            databaseMigrator.ConnectionString,
            lockTimeout,
            logger,
            cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var pendingMigrations = await databaseMigrator.GetPendingMigrations(cancellationToken);
            LogMigrationPlan(pendingMigrations);

            // Every lock holder invokes migration, even when the previous holder failed or the
            // pending-migration check reported a current schema. The database is the authority.
            await databaseMigrator.Migrate(cancellationToken);
            logger.LogInformation(
                "Database migration completed successfully in {ElapsedMs}ms; {MigrationCount} migration(s) were pending.",
                stopwatch.ElapsedMilliseconds,
                pendingMigrations.Length);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Database migration failed after {ElapsedMs}ms.",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private void LogMigrationPlan(string[] pendingMigrations)
    {
        if (pendingMigrations.Length == 0)
        {
            logger.LogInformation("Database schema is current; invoking migration as a no-op check.");
            return;
        }

        logger.LogInformation(
            "Applying {MigrationCount} pending database migration(s).",
            pendingMigrations.Length);
    }
}
