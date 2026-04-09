using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Application.Jobs;

/// <summary>
/// Quartz listener that enforces mutual exclusion between jobs tagged with the same
/// <see cref="MutexGroupAttribute"/> group name. When a job fires, the listener attempts
/// to acquire a named semaphore for up to <see cref="AcquireTimeout"/>. If the semaphore
/// is not available in time the execution is vetoed and a warning is logged. Once acquired,
/// the semaphore is released when the job finishes (even if it throws).
/// </summary>
public sealed class MutexGroupListener(MutexGroupRegistry registry, ILogger<MutexGroupListener> logger)
    : ITriggerListener, IJobListener
{
    public static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, string> _acquiredByFireInstanceId = new();

    // ── ITriggerListener ──────────────────────────────────────────────────────

    public string Name => nameof(MutexGroupListener);

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var mutexGroup = context.JobDetail.JobType.GetCustomAttributes(typeof(MutexGroupAttribute), inherit: false)
            .OfType<MutexGroupAttribute>()
            .FirstOrDefault();

        if (mutexGroup is null)
            return false;

        var acquired = await registry.TryAcquireAsync(mutexGroup.GroupName, AcquireTimeout, cancellationToken);

        if (!acquired)
        {
            logger.LogWarning(
                "Job '{JobKey}' skipped: mutex group '{GroupName}' was not released within {TimeoutSeconds}s.",
                context.JobDetail.Key,
                mutexGroup.GroupName,
                AcquireTimeout.TotalSeconds);
            return true; // veto — skip this execution
        }

        _acquiredByFireInstanceId[context.FireInstanceId] = mutexGroup.GroupName;
        return false;
    }

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // ── IJobListener ──────────────────────────────────────────────────────────

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        if (_acquiredByFireInstanceId.TryRemove(context.FireInstanceId, out var groupName))
            registry.Release(groupName);

        return Task.CompletedTask;
    }
}
