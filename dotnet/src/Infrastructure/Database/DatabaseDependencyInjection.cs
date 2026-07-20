using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Options;

namespace Infrastructure.Database;

public static class DatabaseDependencyInjection
{
    private const string ConnectionStringConfigurationKey = "SkuSync";

    extension(IHost app)
    {
        public async Task ApplyDatabaseMigrations(CancellationToken cancellationToken = default)
        {
            var applicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                applicationLifetime.ApplicationStopping);

            await using var scope = app.Services.CreateAsyncScope();
            var migrationRunner = scope.ServiceProvider.GetRequiredService<DatabaseMigrationRunner>();
            await migrationRunner.Run(linkedCancellation.Token);
        }
    }

    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddDatabase()
        {
            var skuSyncConnectionString = builder.GetConnectionStringOrThrow(ConnectionStringConfigurationKey);

            builder.AddOptionsFromConfiguration<DatabaseMigrationOptions>(
                DatabaseMigrationOptions.SectionKey);

            builder.Services.AddHealthChecks()
                .AddNpgSql(
                    skuSyncConnectionString,
                    name: "postgres",
                    tags: ["ready", "db"]);

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(skuSyncConnectionString));
            builder.Services.AddScoped<IDatabaseMigrator, EfCoreDatabaseMigrator>();
            builder.Services.AddScoped<DatabaseMigrationRunner>();

            return builder;
        }
    }
}
