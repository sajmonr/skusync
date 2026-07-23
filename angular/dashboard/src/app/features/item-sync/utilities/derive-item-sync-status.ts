import { ItemSyncListItem, ItemSyncStatus } from '../models/item-sync-list-item';

export function deriveItemSyncStatus(item: ItemSyncListItem): ItemSyncStatus {
  if (item.pendingShopifySync || item.skulabs?.pendingSkulabsSync) {
    return 'pending-sync';
  }

  if (item.skulabs === null) {
    return 'missing-in-skulabs';
  }

  return item.displayName === item.skulabs.title ? 'in-sync' : 'out-of-sync';
}
