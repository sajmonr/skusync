import { fetchVariantInformation, type VariantInformation } from "../api/variantInformation";
import { type LookupState, useLookup } from "./useLookup";

/** Loads the SkuLabs information for one Shopify variant. */
export function useVariantInformation(
  variantGid: string | undefined,
): LookupState<VariantInformation> {
  return useLookup(variantGid, fetchVariantInformation);
}
