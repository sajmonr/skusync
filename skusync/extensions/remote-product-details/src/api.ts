import { API_BASE_URL, apiUrl } from "../../../shared/config/api";

/**
 * Why a request failed. The component maps each of these to its own message, so a variant that
 * simply hasn't synced yet never reads as a broken extension.
 */
export const FailureReason = {
  /** No session token, or the API rejected the one we sent. */
  Unauthenticated: "unauthenticated",
  /** The API has nothing to show for this variant. Deliberately says no more than that. */
  NotFound: "not-found",
  /** The API could not be reached at all — usually a wrong base URL or an untrusted certificate. */
  Unreachable: "unreachable",
  /** Anything else, including 5xx. */
  Unexpected: "unexpected",
} as const;

export type FailureReason = (typeof FailureReason)[keyof typeof FailureReason];

/** The successful response body of `GET /shopify/variant-information`. */
export interface VariantInformation {
  variantId: number;
  skulabsItemId: string;
  skulabsUrl: string;
}

export interface VariantInformationSuccess {
  ok: true;
  data: VariantInformation;
}

export interface VariantInformationFailure {
  ok: false;
  reason: FailureReason;
  /** Extra context for the merchant-facing message, such as the URL that didn't respond. */
  detail?: string;
}

/**
 * Discriminated on `ok`, so narrowing gives access to `data` or to `reason` but never both — the
 * compiler rejects rendering a success body for a failed lookup.
 */
export type VariantInformationResult = VariantInformationSuccess | VariantInformationFailure;

/**
 * Looks up the SkuLabs information for a Shopify product variant.
 *
 * @param variantGid The variant's Admin GraphQL global ID.
 * @param signal Aborts the request when the merchant navigates away.
 * @throws When the request is aborted, so callers can ignore a stale lookup.
 */
export async function fetchVariantInformation(
  variantGid: string,
  signal?: AbortSignal,
): Promise<VariantInformationResult> {
  const token = await shopify.auth.idToken();

  if (!token) {
    return { ok: false, reason: FailureReason.Unauthenticated };
  }

  const url = apiUrl(`shopify/variant-information?variantId=${encodeURIComponent(variantGid)}`);
  let response: Response;

  try {
    response = await fetch(url, {
      method: "GET",
      headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
      signal,
    });
  } catch (error) {
    // fetch only rejects for transport-level problems: DNS, TLS, CORS, or an abort.
    if (error instanceof Error && error.name === "AbortError") {
      throw error;
    }

    return { ok: false, reason: FailureReason.Unreachable, detail: API_BASE_URL };
  }

  if (response.ok) {
    return { ok: true, data: (await response.json()) as VariantInformation };
  }

  if (response.status === 404) {
    return { ok: false, reason: FailureReason.NotFound };
  }

  if (response.status === 401 || response.status === 403) {
    return { ok: false, reason: FailureReason.Unauthenticated };
  }

  return { ok: false, reason: FailureReason.Unexpected, detail: `HTTP ${response.status}` };
}
