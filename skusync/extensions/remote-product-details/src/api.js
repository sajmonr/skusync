import { API_BASE_URL } from "./config";

/**
 * Why a request failed. The component maps each of these to its own message, so a variant that
 * simply hasn't synced yet never reads as a broken extension.
 */
export const FailureReason = {
  /** No session token, or the API rejected the one we sent. */
  Unauthenticated: "unauthenticated",
  /** The API has no SkuLabs item linked to this variant yet. */
  NotLinked: "not-linked",
  /** The API could not be reached at all — usually a wrong base URL or an untrusted certificate. */
  Unreachable: "unreachable",
  /** Anything else, including 5xx. */
  Unexpected: "unexpected",
};

/**
 * Looks up the SkuLabs information for a Shopify product variant.
 *
 * @param {string} variantGid The variant's Admin GraphQL global ID.
 * @param {AbortSignal} [signal] Aborts the request when the merchant navigates away.
 * @returns {Promise<{ok: true, data: {variantId: number, skulabsItemId: string, skulabsUrl: string}}
 *   | {ok: false, reason: string, detail?: string}>}
 */
export async function fetchVariantInformation(variantGid, signal) {
  const token = await shopify.auth.idToken();

  if (!token) {
    return { ok: false, reason: FailureReason.Unauthenticated };
  }

  const url = `${API_BASE_URL}/shopify/variant-information?variantId=${encodeURIComponent(variantGid)}`;
  let response;

  try {
    response = await fetch(url, {
      method: "GET",
      headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
      signal,
    });
  } catch (error) {
    // fetch only rejects for transport-level problems: DNS, TLS, CORS, or an abort.
    if (error?.name === "AbortError") {
      throw error;
    }

    return { ok: false, reason: FailureReason.Unreachable, detail: API_BASE_URL };
  }

  if (response.ok) {
    return { ok: true, data: await response.json() };
  }

  if (response.status === 404) {
    return { ok: false, reason: FailureReason.NotLinked };
  }

  if (response.status === 401 || response.status === 403) {
    return { ok: false, reason: FailureReason.Unauthenticated };
  }

  return { ok: false, reason: FailureReason.Unexpected, detail: `HTTP ${response.status}` };
}
