namespace Application.Jobs.Maintenance;

/// <summary>
/// Declares the position of an <see cref="IMaintenanceTask"/> in the execution sequence run
/// by <see cref="ProductMaintenanceJob"/>. Tasks with this attribute run first in ascending
/// <see cref="Order"/>; tasks without it run afterwards in DI registration order. Ties are
/// broken by DI registration order.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TaskOrderAttribute(int order) : Attribute
{
    /// <summary>The ordinal position. Lower values run earlier.</summary>
    public int Order { get; } = order;
}
