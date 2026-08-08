namespace Application.Sync.Merge;

/// <summary>
/// The registered <see cref="IMergeRule"/> set, validated once and then applied to each item.
/// <para>
/// Validation happens in the constructor, so a chain with two rules claiming the same field fails
/// at startup with both names in the message rather than producing quietly wrong values in
/// production. Field authority that depends on which rule happens to run last is exactly the class
/// of bug this mechanism replaced.
/// </para>
/// </summary>
public sealed class MergeRuleChain
{
    private readonly IMergeRule[] _rules;

    public MergeRuleChain(IEnumerable<IMergeRule> rules)
    {
        _rules = rules.ToArray();

        var owners = new Dictionary<ItemField, string>();
        foreach (var rule in _rules)
        {
            foreach (var field in rule.OwnedFields)
            {
                var ruleName = rule.GetType().Name;
                if (owners.TryGetValue(field, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Merge rules '{existing}' and '{ruleName}' both claim {nameof(ItemField)}.{field}. "
                        + "Each field must have exactly one owning rule, otherwise which value wins "
                        + "depends on registration order.");
                }

                owners[field] = ruleName;
            }
        }

        UnownedFields = Enum.GetValues<ItemField>().Where(field => !owners.ContainsKey(field)).ToArray();
    }

    /// <summary>
    /// Fields no registered rule decides. Not an error — such a field simply keeps whatever it
    /// already holds — but worth surfacing, since it is far more often an oversight than a choice.
    /// </summary>
    public IReadOnlyCollection<ItemField> UnownedFields { get; }

    public async ValueTask<MergeResult> Apply(MergeContext context, CancellationToken cancellationToken = default)
    {
        foreach (var rule in _rules)
        {
            await rule.Apply(context, cancellationToken);
        }

        return context.Result;
    }
}
