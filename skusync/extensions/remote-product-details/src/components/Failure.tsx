import { FailureReason, type VariantInformationFailure } from "../api";

export function Failure({ failure }: { failure: VariantInformationFailure }) {
  const {
    i18n,
    extension: { target },
  } = shopify;

  // Having nothing to show is a normal state, not a fault, so it gets quiet text rather than a
  // critical banner. The copy says only that there is nothing to display — why there isn't is
  // internal to SkuSync and deliberately not surfaced here.
  if (failure.reason === FailureReason.NotFound) {
    return <s-text color="subdued">{i18n.translate("notFound")}</s-text>;
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
