namespace Application.Sync.Merge;

/// <summary>
/// One field-authority decision. Implementations answer a single question — "given what both
/// systems say and what we currently believe, what should this field be?" — and write the answer to
/// <see cref="MergeContext.Result"/>.
/// <para>
/// Rules run in sequence and each sees the running result, so a later rule can build on an earlier
/// one's decision. They may not, however, overlap: <see cref="OwnedFields"/> is validated at
/// startup so two rules can never both claim a field. Authority is a property of a field, not of a
/// position in a list, and leaving it to ordering would make the outcome depend on registration
/// order — invisible at the call site and silently altered by reordering a DI file.
/// </para>
/// <para>
/// A rule that decides to change nothing simply does not assign; the result is pre-seeded with the
/// current value, so silence means "leave it alone" rather than "blank it".
/// </para>
/// </summary>
public interface IMergeRule
{
    /// <summary>
    /// The fields this rule may write. Assigning anything outside this set is a programming error
    /// the chain will not catch, so keep it honest.
    /// </summary>
    IReadOnlyCollection<ItemField> OwnedFields { get; }

    /// <summary>Decides this rule's fields for one item.</summary>
    ValueTask Apply(MergeContext context, CancellationToken cancellationToken = default);
}
