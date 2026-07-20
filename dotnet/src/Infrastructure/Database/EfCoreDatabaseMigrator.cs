using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database;

internal sealed class EfCoreDatabaseMigrator(ApplicationDbContext dbContext) : IDatabaseMigrator
{
    public string ConnectionString => dbContext.Database.GetConnectionString()
        ?? throw new InvalidOperationException("The database connection string is not configured.");

    public async Task<string[]> GetPendingMigrations(CancellationToken cancellationToken) =>
        (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

    public Task Migrate(CancellationToken cancellationToken) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}
