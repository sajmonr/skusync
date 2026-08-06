import { type ApiFailure, FailureReason } from "../api/result";

/**
 * Renders a failed lookup. `nothingToShow` is the copy for the not-found case, which differs by
 * surface — one variant on the variant page, a product's variants on the product page.
 */
export function Failure({
  failure,
  nothingToShow,
}: {
  failure: ApiFailure;
  nothingToShow: string;
}) {
  const { i18n } = shopify;

  // Having nothing to show is a normal state, not a fault, so it gets quiet text rather than a
  // critical banner. The copy says only that there is nothing to display — why there isn't is
  // internal to SkuSync and deliberately not surfaced here.
  if (failure.reason === FailureReason.NotFound) {
    return <s-text color="subdued">{nothingToShow}</s-text>;
  }

  const tone = failure.reason === FailureReason.Unauthenticated ? "warning" : "critical";

  return (
    <s-banner tone={tone} heading={i18n.translate(`failure.${failure.reason}.heading`)}>
      <s-paragraph>
        {i18n.translate(`failure.${failure.reason}.body`, { detail: failure.detail ?? "" })}
      </s-paragraph>
    </s-banner>
  );
}
