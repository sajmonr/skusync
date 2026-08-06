import type { ProductVariantInformation } from "../api/productInformation";
import { SkulabsLink } from "./SkulabsLink";

/**
 * One variant in the product-level list. The SKU leads because that is what SkuLabs is keyed on; the
 * display name repeats the product title, so it sits underneath as supporting detail.
 */
export function VariantRow({ variant }: { variant: ProductVariantInformation }) {
  const { i18n } = shopify;

  return (
    <s-stack direction="block" gap="small-500">
      <s-text type="strong">{variant.sku || i18n.translate("variant.noSku")}</s-text>
      <s-text color="subdued">{variant.title}</s-text>
      {variant.skulabsUrl ? (
        <SkulabsLink url={variant.skulabsUrl} label={i18n.translate("viewInSkulabs")} />
      ) : (
        <s-text color="subdued">{i18n.translate("variant.notLinked")}</s-text>
      )}
    </s-stack>
  );
}
