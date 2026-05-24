namespace Integration.Skulabs.Items;

/// <summary>
/// Fields that can be updated on a SkuLabs inventory item.
/// Pair with an item id via <see cref="SkulabsItemUpdateWithId"/> when calling
/// <see cref="ISkulabsItemClient.UpdateItems"/>, or use the
/// <see cref="SkulabsItemClientExtensions.UpdateItem"/> convenience helper to
/// update a single item by id.
/// </summary>
public record class SkulabsItemUpdate(string Name);
