export function Loading() {
  const { i18n } = shopify;

  return (
    <s-stack direction="inline" gap="small" alignItems="center">
      <s-spinner size="base" accessibilityLabel={i18n.translate("loading")} />
      <s-text>{i18n.translate("loading")}</s-text>
    </s-stack>
  );
}
