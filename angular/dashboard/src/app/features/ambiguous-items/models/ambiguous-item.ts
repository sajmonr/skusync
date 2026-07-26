export type AmbiguityReason = 'NoListings' | 'MultipleListings' | 'ListingNotInShopify';

export interface AmbiguousItemListing {
  readonly listingId: string;
  readonly rawVariantId: string;
  readonly skulabsProductId: string;
  readonly resolvedToShopifyVariant: boolean;
  readonly resolvedVariantId: number | null;
  readonly resolvedDisplayName: string | null;
  readonly shopifyUrl: string;
}

export interface AmbiguousItem {
  readonly id: string;
  readonly skulabsItemId: string;
  readonly name: string;
  readonly sku: string;
  readonly upc: string;
  readonly listingCount: number;
  readonly reason: AmbiguityReason;
  readonly status: string;
  readonly firstSeenUtc: string;
  readonly lastSeenUtc: string;
  readonly skulabsUrl: string;
  readonly listings: readonly AmbiguousItemListing[];
}
