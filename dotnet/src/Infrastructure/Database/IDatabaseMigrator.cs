namespace Infrastructure.Database;

internal interface IDatabaseMigrator
{
    string ConnectionString { get; }

    Task<string[]> GetPendingMigrations(CancellationToken cancellationToken);

    Task Migrate(CancellationToken cancellationToken);
}
