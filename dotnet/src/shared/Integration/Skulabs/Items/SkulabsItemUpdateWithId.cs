namespace Integration.Skulabs.Items;

/// <summary>
/// A <see cref="SkulabsItemUpdate"/> together with the SkuLabs item id it targets.
/// Used as the element type for <see cref="ISkulabsItemClient.UpdateItems"/>.
/// </summary>
public record class SkulabsItemUpdateWithId(string Id, string Name) : SkulabsItemUpdate(Name);
