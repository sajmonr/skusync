import { getFromApi } from "./request";
import { type ApiResult, notFound } from "./result";

/** One variant of a product, as returned by `GET /shopify/product-information`. */
export interface ProductVariantInformation {
  variantId: number;
  sku: string;
  title: string;
  /** `null` when the variant has no SkuLabs item to link to. */
  skulabsUrl: string | null;
}

/** The successful response body of `GET /shopify/product-information`. */
export interface ProductInformation {
  productId: number;
  variants: ProductVariantInformation[];
}

/**
 * Looks up every variant of a Shopify product that SkuSync knows about.
 *
 * A response carrying no variants is folded into a not-found failure so callers never have to render
 * an empty card — the API answers 404 for a product it holds nothing for, and this covers the same
 * ground for a body that arrives empty anyway.
 *
 * @param productGid The product's Admin GraphQL global ID.
 * @param signal Aborts the request when the merchant navigates away.
 */
export async function fetchProductInformation(
  productGid: string,
  signal?: AbortSignal,
): Promise<ApiResult<ProductInformation>> {
  const result = await getFromApi<ProductInformation>(
    `shopify/product-information?productId=${encodeURIComponent(productGid)}`,
    signal,
  );

  if (result.ok && result.data.variants.length === 0) {
    return notFound;
  }

  return result;
}
