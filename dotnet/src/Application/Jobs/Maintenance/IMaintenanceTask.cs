namespace Application.Jobs.Maintenance;

/// <summary>
/// A unit of recurring maintenance work executed by <see cref="ProductMaintenanceJob"/>.
/// Tasks are run sequentially in the order they were registered with the DI container.
/// A task failure is isolated by the orchestrator — the exception is logged and the next
/// task still runs — so implementations may surface errors by throwing or by handling
/// them internally as appropriate for the operation.
/// </summary>
public interface IMaintenanceTask
{
    /// <summary>
    /// Gets a short, human-readable name used in log messages so operators can tell which
    /// task ran (and which one failed). Conventionally the implementing class name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the maintenance task. Implementations should respect
    /// <paramref name="cancellationToken"/> so the orchestrating job can cancel cleanly.
    /// </summary>
    Task Execute(CancellationToken cancellationToken);
}
