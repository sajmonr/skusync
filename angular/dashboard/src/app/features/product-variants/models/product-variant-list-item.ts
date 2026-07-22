export interface ProductVariantListItem {
  readonly id: string;
  readonly productId: number;
  readonly variantId: number;
  readonly displayName: string;
  readonly sku: string;
  readonly barcode: string;
  readonly pendingSync: boolean;
  readonly failedSyncAttempts: number;
  readonly active: boolean;
  readonly updatedOnUtc: string;
}
