import "@shopify/ui-extensions/preact";
import { render } from "preact";

// 1. Explicitly point your default export to the product-variant-details render target
export default async () => {
  render(<Extension />, document.body);
};

function Extension() {
  // 2. Destructure the shopify global object.
  // Under the hood, 'shopify.data' automatically changes shape based on the target page.
  const {
    i18n,
    data,
    extension: { target },
  } = shopify;

  // For 'admin.product-variant-details.block.render', data contains the selected variant info
  const variantId = data?.selected[0]?.id;

  console.log("Current Variant Page Data:", data);

  return (
    <s-admin-block heading='Variant Manager Insights'>
      <s-stack direction='block' gap='small'>
        <s-text type='strong'>{i18n.translate("welcome", { target })}</s-text>
        <s-text>Active Variant ID: {variantId || "Loading..."}</s-text>
      </s-stack>
    </s-admin-block>
  );
}
