import { useVariantInformation } from "../hooks/useVariantInformation";
import { Failure } from "./Failure";
import { Loading } from "./Loading";
import { SkulabsButton } from "./SkulabsButton";

/**
 * The way out to SkuLabs for the single variant a merchant is looking at.
 *
 * `alignItems="start"` keeps the button at its content width rather than stretching it across the card.
 */
export function VariantDetails({ variantGid }: { variantGid: string | undefined }) {
  const { i18n } = shopify;
  const state = useVariantInformation(variantGid);

  return (
    <s-stack direction="block" gap="small-200" alignItems="start">
      {state.status === "loading" && <Loading message={i18n.translate("loading.variant")} />}
      {state.status === "loaded" && (
        <SkulabsButton url={state.data.skulabsUrl} label={i18n.translate("openInSkulabs")} />
      )}
      {state.status === "failed" && (
        <Failure failure={state.failure} nothingToShow={i18n.translate("notFound.variant")} />
      )}
    </s-stack>
  );
}
