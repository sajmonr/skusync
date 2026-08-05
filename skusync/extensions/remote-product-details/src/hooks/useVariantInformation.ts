import { useEffect, useState } from "preact/hooks";
import {
  fetchVariantInformation,
  FailureReason,
  type VariantInformation,
  type VariantInformationFailure,
  type VariantInformationResult,
} from "../api";

/**
 * What the block is currently showing. Discriminated on `status` so a caller can only reach
 * `information` once the lookup has succeeded and `failure` once it has failed.
 */
export type LookupState =
  | { status: "loading" }
  | { status: "loaded"; information: VariantInformation }
  | { status: "failed"; failure: VariantInformationFailure };

/**
 * Loads the SkuLabs information for `variantGid`, re-running whenever the merchant moves to a
 * different variant and abandoning the in-flight request when they do.
 */
export function useVariantInformation(variantGid: string | undefined): LookupState {
  const [state, setState] = useState<LookupState>({ status: "loading" });

  useEffect(() => {
    if (!variantGid) {
      setState({ status: "loading" });
      return undefined;
    }

    const controller = new AbortController();
    setState({ status: "loading" });

    fetchVariantInformation(variantGid, controller.signal)
      .then((result) => setState(toLookupState(result)))
      .catch((error: unknown) => {
        if (error instanceof Error && error.name === "AbortError") {
          return;
        }

        setState({
          status: "failed",
          failure: {
            ok: false,
            reason: FailureReason.Unexpected,
            detail: error instanceof Error ? error.message : undefined,
          },
        });
      });

    return () => controller.abort();
  }, [variantGid]);

  return state;
}

function toLookupState(result: VariantInformationResult): LookupState {
  if (result.ok) {
    return { status: "loaded", information: result.data };
  }

  return { status: "failed", failure: result };
}
