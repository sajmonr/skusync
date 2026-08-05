import type { VariantInformation } from "../api";

export function SkulabsLink({ information }: { information: VariantInformation }) {
  const { i18n } = shopify;

  return (
    <s-stack direction="block" gap="small-200">
      <s-link href={information.skulabsUrl} target="_blank">
        {i18n.translate("openInSkulabs")}
      </s-link>
      <s-text color="subdued">{i18n.translate("itemId", { id: information.skulabsItemId })}</s-text>
    </s-stack>
  );
}
