import { useProductInformation } from "../hooks/useProductInformation";
import { Failure } from "./Failure";
import { Loading } from "./Loading";
import { VariantList } from "./VariantList";

/**
 * Every variant of the product a merchant is looking at. This is what the variant-level block cannot
 * do: a product with one variant has no variant page to render on, and a product with many would cost
 * one page visit per variant.
 */
export function ProductVariants({ productGid }: { productGid: string | undefined }) {
  const { i18n } = shopify;
  const state = useProductInformation(productGid);

  return (
    <s-stack direction="block" gap="small-200">
      {state.status === "loading" && <Loading message={i18n.translate("loading.product")} />}
      {state.status === "loaded" && <VariantList variants={state.data.variants} />}
      {state.status === "failed" && (
        <Failure failure={state.failure} nothingToShow={i18n.translate("notFound.product")} />
      )}
    </s-stack>
  );
}
