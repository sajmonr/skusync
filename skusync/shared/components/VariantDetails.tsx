import { useVariantInformation } from "../hooks/useVariantInformation";
import { Failure } from "./Failure";
import { Loading } from "./Loading";
import { SkulabsLink } from "./SkulabsLink";

/** The SkuLabs link for the single variant a merchant is looking at. */
export function VariantDetails({ variantGid }: { variantGid: string | undefined }) {
  const { i18n } = shopify;
  const state = useVariantInformation(variantGid);

  return (
    <s-stack direction="block" gap="small-200">
      {state.status === "loading" && <Loading message={i18n.translate("loading.variant")} />}
      {state.status === "loaded" && (
        <SkulabsLink url={state.data.skulabsUrl} label={i18n.translate("openInSkulabs")} />
      )}
      {state.status === "failed" && (
        <Failure failure={state.failure} nothingToShow={i18n.translate("notFound.variant")} />
      )}
    </s-stack>
  );
}
