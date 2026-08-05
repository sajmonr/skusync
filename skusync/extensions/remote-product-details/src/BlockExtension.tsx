import "@shopify/ui-extensions/preact";
import { render } from "preact";
import { Failure } from "./components/Failure";
import { Loading } from "./components/Loading";
import { SkulabsLink } from "./components/SkulabsLink";
import { useVariantInformation } from "./hooks/useVariantInformation";

export default async () => {
  render(<Extension />, document.body);
};

function Extension() {
  const { i18n, data } = shopify;

  // The product-variant-details target puts the variant the merchant is viewing in `data.selected`.
  const variantGid = data?.selected?.[0]?.id;
  const state = useVariantInformation(variantGid);

  return (
    <s-admin-block heading={i18n.translate("heading")}>
      <s-stack direction="block" gap="small-200">
        {state.status === "loading" && <Loading />}
        {state.status === "loaded" && <SkulabsLink information={state.information} />}
        {state.status === "failed" && <Failure failure={state.failure} />}
      </s-stack>
    </s-admin-block>
  );
}
