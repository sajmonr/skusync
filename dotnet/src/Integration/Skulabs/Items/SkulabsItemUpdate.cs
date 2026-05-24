namespace Integration.Skulabs.Items;

/// <summary>
/// Mutable fields supplied to <see cref="ISkulabsItemClient.UpdateItem"/> when
/// updating a SkuLabs inventory item.
/// </summary>
public readonly record struct SkulabsItemUpdate(string Name);
