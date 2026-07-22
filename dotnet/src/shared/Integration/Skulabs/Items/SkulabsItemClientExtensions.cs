namespace Integration.Skulabs.Items;

/// <summary>
/// Convenience overloads on <see cref="ISkulabsItemClient"/>.
/// </summary>
public static class SkulabsItemClientExtensions
{
    extension(ISkulabsItemClient client)
    {
        /// <summary>
        /// Updates a single SkuLabs item by id. Wraps the call into the bulk-upsert
        /// endpoint so callers updating just one item don't have to build a singleton
        /// collection themselves.
        /// </summary>
        public Task UpdateItem(string itemId, SkulabsItemUpdate update) =>
            client.UpdateItems([new SkulabsItemUpdateWithId(itemId, update.Name)]);
    }
}
