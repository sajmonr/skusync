import { ItemPropertyComparison, ItemSyncListItem } from '../models/item-sync-list-item';

export function buildItemPropertyComparisons(
  item: ItemSyncListItem,
): readonly ItemPropertyComparison[] {
  return [
    compareShopifyField('Title', item.displayName, item.skulabs?.title ?? null),
    compareSkulabsField('SKU', item.sku, item.skulabs?.sku ?? null),
    compareSkulabsField('Barcode', item.barcode, item.skulabs?.barcode ?? null),
  ];
}

function compareShopifyField(
  property: string,
  shopifyValue: string,
  skulabsValue: string | null,
): ItemPropertyComparison {
  if (skulabsValue === null) {
    return { property, shopifyValue, skulabsValue, state: 'missing' };
  }

  return {
    property,
    shopifyValue,
    skulabsValue,
    state: shopifyValue === skulabsValue ? 'matched' : 'different',
  };
}

function compareSkulabsField(
  property: string,
  shopifyValue: string,
  skulabsValue: string | null,
): ItemPropertyComparison {
  if (skulabsValue === null) {
    return { property, shopifyValue, skulabsValue, state: 'missing' };
  }

  return {
    property,
    shopifyValue,
    skulabsValue,
    state: shopifyValue === skulabsValue ? 'matched' : 'authoritative',
  };
}
