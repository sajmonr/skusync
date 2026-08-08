namespace Application.Sync.Merge;

/// <summary>
/// One field as an external system last reported it, keeping "we did not hear" apart from "they
/// said it is empty".
/// <para>
/// The distinction is the single most regression-prone rule in this codebase, so it lives in the
/// type rather than in each rule's head. A blank SKU in SkuLabs means SkuLabs has no SKU on record,
/// not that ours should be erased; a blank <em>location</em>, by contrast, is a real statement that
/// the item sits in no bin. Rules pick <see cref="HasValue"/> or <see cref="IsObserved"/> per field
/// and the difference stays explicit at the point of decision.
/// </para>
/// </summary>
public readonly record struct ObservedValue
{
    private ObservedValue(bool isObserved, string value)
    {
        IsObserved = isObserved;
        Value = value;
    }

    /// <summary>Nothing was heard — no linked item, or the field was never requested.</summary>
    public static readonly ObservedValue Unobserved = new(false, "");

    /// <summary>An observation, including an explicitly empty one.</summary>
    public static ObservedValue Of(string? value) => new(true, value ?? "");

    /// <summary>
    /// An observation that may be absent: <c>null</c> means the field was not requested at all,
    /// which is how the SkuLabs client reports a location it never asked for.
    /// </summary>
    public static ObservedValue OfNullable(string? value) =>
        value is null ? Unobserved : Of(value);

    /// <summary>Whether the system said anything about this field at all.</summary>
    public bool IsObserved { get; }

    /// <summary>What it said. Empty when unobserved, so callers that do not care cannot trip over null.</summary>
    public string Value { get; }

    /// <summary>
    /// Whether this counts as a claim on the field — observed <em>and</em> non-empty. The right
    /// test for values where blank means "none on record" rather than "set it to blank".
    /// </summary>
    public bool HasValue => IsObserved && Value.Length > 0;
}
