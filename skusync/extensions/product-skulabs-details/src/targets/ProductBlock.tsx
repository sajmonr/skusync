import "@shopify/ui-extensions/preact";
import { render } from "preact";
import { ProductVariants } from "../../../../shared/components/ProductVariants";

export default async () => {
  render(<Extension />, document.body);
};

function Extension() {
  const { i18n, data } = shopify;

  // The product-details target puts the product the merchant is viewing in `data.selected`.
  const productGid = data?.selected?.[0]?.id;

  return (
    <s-admin-block heading={i18n.translate("heading")}>
      <ProductVariants productGid={productGid} />
    </s-admin-block>
  );
}
