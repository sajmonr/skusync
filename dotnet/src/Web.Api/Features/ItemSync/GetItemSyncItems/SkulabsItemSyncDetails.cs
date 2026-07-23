namespace Web.Api.Features.ItemSync.GetItemSyncItems;

public readonly record struct SkulabsItemSyncDetails(
    string Id,
    string Title,
    string Sku,
    string Barcode,
    bool PendingSkulabsSync,
    string Url);
