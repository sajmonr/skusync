import "@shopify/ui-extensions/preact";
import { render } from "preact";
import { VariantDetails } from "../../../../shared/components/VariantDetails";

export default async () => {
  render(<Extension />, document.body);
};

function Extension() {
  const { i18n, data } = shopify;

  // The product-variant-details target puts the variant the merchant is viewing in `data.selected`.
  const variantGid = data?.selected?.[0]?.id;

  return (
    <s-admin-block heading={i18n.translate("heading")}>
      <VariantDetails variantGid={variantGid} />
    </s-admin-block>
  );
}
