import { useEffect, useState } from "preact/hooks";
import { type ApiFailure, type ApiResult, FailureReason } from "../api/result";

/**
 * What a lookup is currently showing. Discriminated on `status` so a caller can only reach `data`
 * once the lookup has succeeded and `failure` once it has failed.
 */
export type LookupState<T> =
  | { status: "loading" }
  | { status: "loaded"; data: T }
  | { status: "failed"; failure: ApiFailure };

/** Fetches the data a lookup renders, aborting when the merchant navigates away. */
export type Lookup<T> = (resourceGid: string, signal: AbortSignal) => Promise<ApiResult<T>>;

/**
 * Runs `lookup` for `resourceGid`, re-running whenever the merchant moves to a different resource and
 * abandoning the in-flight request when they do. Stays in `loading` while the gid is undefined, which
 * is what the render targets hand over for the tick before the host has supplied their selection.
 */
export function useLookup<T>(
  resourceGid: string | undefined,
  lookup: Lookup<T>,
): LookupState<T> {
  const [state, setState] = useState<LookupState<T>>({ status: "loading" });

  useEffect(() => {
    if (!resourceGid) {
      setState({ status: "loading" });
      return undefined;
    }

    const controller = new AbortController();
    // Aborting is advisory: a lookup that resolves before the abort lands, or one that ignores the
    // signal, still settles its promise and would write over the newer resource's state. This flag is
    // what makes the write conditional on the effect still being the current one.
    let current = true;

    setState({ status: "loading" });

    lookup(resourceGid, controller.signal)
      .then((result) => {
        if (current) {
          setState(toLookupState(result));
        }
      })
      .catch((error: unknown) => {
        if (!current || (error instanceof Error && error.name === "AbortError")) {
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

    return () => {
      current = false;
      controller.abort();
    };
  }, [resourceGid, lookup]);

  return state;
}

function toLookupState<T>(result: ApiResult<T>): LookupState<T> {
  if (result.ok) {
    return { status: "loaded", data: result.data };
  }

  return { status: "failed", failure: result };
}
