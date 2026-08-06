import type { ProductVariantInformation } from "../api/productInformation";
import { SkulabsButton } from "./SkulabsButton";

/**
 * One variant in the product-level list: its display name as the row heading, and the way out to
 * SkuLabs.
 *
 * `alignItems="start"` keeps the button at its content width — `s-button` fills the inline space it is
 * given, and its `inlineSize` property isn't exposed to JSX, so the stack has to do the constraining.
 */
export function VariantRow({ variant }: { variant: ProductVariantInformation }) {
  const { i18n } = shopify;

  return (
    <s-stack direction="block" gap="small-300" alignItems="start">
      <s-heading>{variant.title}</s-heading>
      {variant.skulabsUrl ? (
        <SkulabsButton url={variant.skulabsUrl} label={i18n.translate("openInSkulabs")} />
      ) : (
        <s-text color="subdued">{i18n.translate("variant.notLinked")}</s-text>
      )}
    </s-stack>
  );
}
