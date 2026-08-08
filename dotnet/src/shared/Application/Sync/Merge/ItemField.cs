namespace Application.Sync.Merge;

/// <summary>
/// The fields a merge rule can decide. Naming them lets a rule declare what it governs, which is
/// what makes overlapping ownership detectable at startup rather than as a silent last-writer-wins
/// at runtime.
/// </summary>
public enum ItemField
{
    Sku,
    Barcode,
    Title,
    Location
}
