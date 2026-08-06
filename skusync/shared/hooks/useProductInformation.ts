import { fetchProductInformation, type ProductInformation } from "../api/productInformation";
import { type LookupState, useLookup } from "./useLookup";

/** Loads the SkuLabs information for every variant of one Shopify product. */
export function useProductInformation(
  productGid: string | undefined,
): LookupState<ProductInformation> {
  return useLookup(productGid, fetchProductInformation);
}
