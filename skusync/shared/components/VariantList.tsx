import type { ProductVariantInformation } from "../api/productInformation";
import { VariantRow } from "./VariantRow";

/** Every variant of a product, in the order the API returned them. */
export function VariantList({ variants }: { variants: ProductVariantInformation[] }) {
  return (
    <s-stack direction="block" gap="base">
      {variants.map((variant) => (
        <VariantRow key={variant.variantId} variant={variant} />
      ))}
    </s-stack>
  );
}
