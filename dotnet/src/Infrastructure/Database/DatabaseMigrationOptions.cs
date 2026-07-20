using System.ComponentModel.DataAnnotations;

namespace Infrastructure.Database;

public class DatabaseMigrationOptions
{
    public const string SectionKey = "DatabaseMigration";

    [Range(1, 1800)]
    public int LockTimeoutSeconds { get; init; } = 120;
}
