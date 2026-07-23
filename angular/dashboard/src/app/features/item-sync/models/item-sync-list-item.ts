export type ItemSyncStatus = 'in-sync' | 'out-of-sync' | 'missing-in-skulabs' | 'pending-sync';

export type ComparisonState = 'matched' | 'different' | 'authoritative' | 'missing';

export interface ItemPropertyComparison {
  readonly property: string;
  readonly shopifyValue: string | null;
  readonly skulabsValue: string | null;
  readonly state: ComparisonState;
}

export interface SkulabsItemSyncDetails {
  readonly id: string;
  readonly title: string;
  readonly sku: string;
  readonly barcode: string;
  readonly pendingSkulabsSync: boolean;
  readonly url: string;
}

export interface ItemSyncListItem {
  readonly id: string;
  readonly displayName: string;
  readonly shopifyId: number;
  readonly sku: string;
  readonly barcode: string;
  readonly pendingShopifySync: boolean;
  readonly shopifyUrl: string;
  readonly skulabs: SkulabsItemSyncDetails | null;
}
