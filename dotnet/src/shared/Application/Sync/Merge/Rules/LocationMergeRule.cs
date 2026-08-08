namespace Application.Sync.Merge.Rules;

/// <summary>
/// Decides the bin location: whatever SkuLabs reported, whenever it reported anything.
/// <para>
/// This is the one field where an empty observation is a real statement rather than an absence.
/// "No bin" is a fact about the item and has to be able to clear a stored location, so this rule
/// tests <see cref="ObservedValue.IsObserved"/> where the code rules test
/// <see cref="ObservedValue.HasValue"/>. The genuinely absent case — no warehouse configured, so the
/// field was never requested — arrives as unobserved and correctly leaves the stored value alone.
/// </para>
/// <para>
/// <b>Known gap.</b> Once a location can be edited from Shopify, that edit lands in the desired
/// state and this rule will overwrite it with SkuLabs' older value on the next sync if the push has
/// not happened yet. Closing that needs an explicit marker for "locally overridden, not yet pushed";
/// deriving it from the pending flag will not work, because the flag is itself computed from the
/// desired-versus-mirror comparison and so cannot distinguish which side moved. Left as-is
/// deliberately: no edit path exists yet, and speculative three-way logic would be untestable.
/// </para>
/// </summary>
public sealed class LocationMergeRule : IMergeRule
{
    public IReadOnlyCollection<ItemField> OwnedFields { get; } = [ItemField.Location];

    public ValueTask Apply(MergeContext context, CancellationToken cancellationToken = default)
    {
        if (context.Skulabs.Location.IsObserved)
        {
            context.Result.Location = context.Skulabs.Location.Value;
        }

        return ValueTask.CompletedTask;
    }
}
