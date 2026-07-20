namespace Infrastructure.Database;

public class DatabaseMigrationLockTimeoutException(TimeSpan timeout)
    : TimeoutException($"The database migration lock was not acquired within {timeout}.")
{
    public TimeSpan Timeout { get; } = timeout;
}
