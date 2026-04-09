namespace Application.Jobs;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MutexGroupAttribute(string groupName) : Attribute
{
    public string GroupName { get; } = groupName;
}
