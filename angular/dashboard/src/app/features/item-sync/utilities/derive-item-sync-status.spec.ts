import { ItemSyncListItem } from '../models/item-sync-list-item';
import { deriveItemSyncStatus } from './derive-item-sync-status';

describe('deriveItemSyncStatus', () => {
  it('should return pending sync when the Shopify write-back is pending', () => {
    expect(
      deriveItemSyncStatus({
        ...createItem(),
        pendingShopifySync: true,
        skulabs: { ...createItem().skulabs!, title: 'Different title' },
      }),
    ).toBe('pending-sync');
  });

  it('should return pending sync when the Skulabs write is pending', () => {
    expect(
      deriveItemSyncStatus({
        ...createItem(),
        skulabs: { ...createItem().skulabs!, pendingSkulabsSync: true },
      }),
    ).toBe('pending-sync');
  });

  it('should return missing in Skulabs when no linked item exists', () => {
    expect(deriveItemSyncStatus({ ...createItem(), skulabs: null })).toBe('missing-in-skulabs');
  });

  it('should return out of sync when a Shopify-authoritative field differs', () => {
    expect(
      deriveItemSyncStatus({
        ...createItem(),
        skulabs: { ...createItem().skulabs!, title: 'Different title' },
      }),
    ).toBe('out-of-sync');
  });

  it('should return in sync when the linked values match', () => {
    expect(deriveItemSyncStatus(createItem())).toBe('in-sync');
  });
});

function createItem(): ItemSyncListItem {
  return {
    id: 'item-1',
    displayName: 'Classic Tee · Navy / M',
    shopifyProductId: 100,
    shopifyVariantId: 200,
    sku: 'TEE-NVY-M',
    barcode: '5061054981324',
    pendingShopifySync: false,
    skulabs: {
      id: '300',
      title: 'Classic Tee · Navy / M',
      sku: 'TEE-NVY-M',
      barcode: '5061054981324',
      pendingSkulabsSync: false,
    },
  };
}
