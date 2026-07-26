using Hangfire;
using Hangfire.PostgreSql;

namespace Application.Jobs;

/// <summary>
/// Central Hangfire storage + serializer configuration, shared by every host that either enqueues
/// (Web.Api) or processes (AppServer) background jobs so both agree on storage. Jobs live in the
/// existing application PostgreSQL database under a dedicated <c>hangfire</c> schema, keeping their
/// tables and internal migrations isolated from the EF Core schema and its coordinated-migration
/// lock.
/// </summary>
internal static class HangfireConfiguration
{
    /// <summary>
    /// Named connection string reused for Hangfire storage. Hangfire shares the application
    /// database but confines itself to <see cref="SchemaName"/>.
    /// </summary>
    public const string ConnectionStringName = "SkuSync";

    private const string SchemaName = "hangfire";

    public static void Configure(IGlobalConfiguration config, string connectionString)
    {
        config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            // User-triggered jobs surface failures to the operator immediately rather than silently
            // retrying up to ten times (Hangfire's default). Individual jobs can opt back into
            // retries with [AutomaticRetry] when that is the right behaviour.
            .UseFilter(new AutomaticRetryAttribute { Attempts = 0 })
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = SchemaName });
    }
}
