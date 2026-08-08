namespace Application.Sync.Merge;

/// <summary>
/// The desired values a merge is building up, seeded from what is already stored and tracking which
/// fields a rule actually moved.
/// <para>
/// Seeding from the current state rather than from empty is deliberate: it makes silence mean "no
/// change". A result that started blank would read as an instruction to erase every field no rule
/// happened to set, so one rule forgetting one branch would wipe data rather than leave it alone.
/// </para>
/// <para>
/// Only genuine changes are recorded, which is what lets the caller write one audit event per real
/// change and skip the database write entirely when a pass decides nothing.
/// </para>
/// </summary>
public sealed class MergeResult
{
    private readonly HashSet<ItemField> _changed = [];
    private string _sku;
    private string _barcode;
    private string _title;
    private string _location;

    /// <summary>
    /// Seeds the result with what is currently stored. Public because <see cref="MergeContext"/> is:
    /// anything that can build a context needs to be able to build the result it carries.
    /// </summary>
    public MergeResult(string sku, string barcode, string title, string location)
    {
        _sku = sku;
        _barcode = barcode;
        _title = title;
        _location = location;
    }

    public string Sku
    {
        get => _sku;
        set => Assign(ItemField.Sku, ref _sku, value);
    }

    public string Barcode
    {
        get => _barcode;
        set => Assign(ItemField.Barcode, ref _barcode, value);
    }

    public string Title
    {
        get => _title;
        set => Assign(ItemField.Title, ref _title, value);
    }

    public string Location
    {
        get => _location;
        set => Assign(ItemField.Location, ref _location, value);
    }

    /// <summary>Fields whose value this merge actually moved.</summary>
    public IReadOnlyCollection<ItemField> ChangedFields => _changed;

    public bool HasChanges => _changed.Count > 0;

    public bool Changed(ItemField field) => _changed.Contains(field);

    private void Assign(ItemField field, ref string slot, string? value)
    {
        var incoming = value ?? "";
        if (string.Equals(slot, incoming, StringComparison.Ordinal))
        {
            return;
        }

        slot = incoming;
        _changed.Add(field);
    }
}
