import "@shopify/ui-extensions/preact";
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { fetchVariantInformation, FailureReason } from "./api";

export default async () => {
  render(<Extension />, document.body);
};

function Extension() {
  const {
    i18n,
    data,
    extension: { target },
  } = shopify;

  // The product-variant-details target puts the variant the merchant is viewing in `data.selected`.
  const variantGid = data?.selected?.[0]?.id;
  const { state, result } = useVariantInformation(variantGid);

  return (
    <s-admin-block heading={i18n.translate("heading")}>
      <s-stack direction="block" gap="small-200">
        {state === "loading" && (
          <s-stack direction="inline" gap="small" alignItems="center">
            <s-spinner size="base" accessibilityLabel={i18n.translate("loading")} />
            <s-text>{i18n.translate("loading")}</s-text>
          </s-stack>
        )}

        {state === "loaded" && <SkulabsLink i18n={i18n} information={result.data} />}

        {state === "failed" && <Failure i18n={i18n} failure={result} target={target} />}
      </s-stack>
    </s-admin-block>
  );
}

function SkulabsLink({ i18n, information }) {
  return (
    <s-stack direction="block" gap="small-200">
      <s-link href={information.skulabsUrl} target="_blank">
        {i18n.translate("openInSkulabs")}
      </s-link>
      <s-text color="subdued">{i18n.translate("itemId", { id: information.skulabsItemId })}</s-text>
    </s-stack>
  );
}

function Failure({ i18n, failure, target }) {
  // A variant that hasn't been linked yet is a normal state, not a fault — say so quietly rather
  // than shouting with a critical banner.
  if (failure.reason === FailureReason.NotLinked) {
    return <s-text color="subdued">{i18n.translate("notLinked")}</s-text>;
  }

  const tone = failure.reason === FailureReason.Unauthenticated ? "warning" : "critical";

  return (
    <s-banner tone={tone} heading={i18n.translate(`failure.${failure.reason}.heading`)}>
      <s-paragraph>
        {i18n.translate(`failure.${failure.reason}.body`, {
          detail: failure.detail ?? "",
          target,
        })}
      </s-paragraph>
    </s-banner>
  );
}

/**
 * Loads the SkuLabs information for `variantGid`, re-running whenever the merchant moves to a
 * different variant and abandoning the in-flight request when they do.
 */
function useVariantInformation(variantGid) {
  const [state, setState] = useState("loading");
  const [result, setResult] = useState(null);

  useEffect(() => {
    if (!variantGid) {
      setState("loading");
      setResult(null);
      return undefined;
    }

    const controller = new AbortController();
    setState("loading");

    fetchVariantInformation(variantGid, controller.signal)
      .then((next) => {
        setResult(next);
        setState(next.ok ? "loaded" : "failed");
      })
      .catch((error) => {
        if (error?.name === "AbortError") {
          return;
        }

        setResult({ ok: false, reason: FailureReason.Unexpected, detail: error?.message });
        setState("failed");
      });

    return () => controller.abort();
  }, [variantGid]);

  return { state, result };
}
