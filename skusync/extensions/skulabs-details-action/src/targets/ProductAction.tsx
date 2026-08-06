import "@shopify/ui-extensions/preact";
import { render } from "preact";
import { ProductVariants } from "../../../../shared/components/ProductVariants";

export default async () => {
  render(<Extension />, document.body);
};

/**
 * The product's variants in a modal launched from the **More actions** menu, rendering the same
 * component as the product-page card so the two cannot drift apart.
 */
function Extension() {
  const { i18n, data } = shopify;

  // The product-details target puts the product the merchant is viewing in `data.selected`.
  const productGid = data?.selected?.[0]?.id;

  return (
    <s-admin-action heading={i18n.translate("heading")}>
      <ProductVariants productGid={productGid} />
      <s-button slot="primary-action" onClick={() => shopify.close()}>
        {i18n.translate("done")}
      </s-button>
    </s-admin-action>
  );
}
