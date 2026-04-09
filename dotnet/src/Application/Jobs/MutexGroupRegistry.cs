using System.Collections.Concurrent;

namespace Application.Jobs;

public sealed class MutexGroupRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public Task<bool> TryAcquireAsync(string groupName, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var semaphore = _semaphores.GetOrAdd(groupName, _ => new SemaphoreSlim(1, 1));
        return semaphore.WaitAsync(timeout, cancellationToken);
    }

    public void Release(string groupName)
    {
        if (_semaphores.TryGetValue(groupName, out var semaphore))
            semaphore.Release();
    }
}
