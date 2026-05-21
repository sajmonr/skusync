namespace Tests.E2E.Infrastructure;

/// <summary>
/// Polls a predicate until it returns true or a timeout elapses. Used to wait for
/// asynchronous side effects (e.g. SlimMessageBus consumers running with
/// EnableBlockingPublish=false) without making tests rely on Thread.Sleep.
/// </summary>
internal static class AsyncWait
{
    public static async Task UntilAsync(
        Func<bool> predicate,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(25);

        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(interval);
        }

        throw new TimeoutException(
            $"Condition was not met within {timeout ?? TimeSpan.FromSeconds(5)}." +
            (because is null ? "" : $" {because}"));
    }
}
