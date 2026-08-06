import { getFromApi } from "./request";
import type { ApiResult } from "./result";

/** The successful response body of `GET /shopify/variant-information`. */
export interface VariantInformation {
  variantId: number;
  skulabsItemId: string;
  skulabsUrl: string;
}

/**
 * Looks up the SkuLabs information for a single Shopify product variant.
 *
 * @param variantGid The variant's Admin GraphQL global ID.
 * @param signal Aborts the request when the merchant navigates away.
 */
export function fetchVariantInformation(
  variantGid: string,
  signal?: AbortSignal,
): Promise<ApiResult<VariantInformation>> {
  return getFromApi(
    `shopify/variant-information?variantId=${encodeURIComponent(variantGid)}`,
    signal,
  );
}
