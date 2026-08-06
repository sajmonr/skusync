import { API_BASE_URL, apiUrl } from "../config/api";
import { type ApiResult, FailureReason } from "./result";

/**
 * Issues an authenticated GET against the SkuSync API and maps the outcome onto {@link ApiResult}.
 *
 * The Shopify session token goes out as a bearer token; the API verifies it itself (HS256, signed
 * with the app's client secret).
 *
 * @param path The API path, relative to the configured base URL.
 * @param signal Aborts the request when the merchant navigates away.
 * @throws When the request is aborted, so callers can ignore a stale lookup.
 */
export async function getFromApi<T>(path: string, signal?: AbortSignal): Promise<ApiResult<T>> {
  const token = await shopify.auth.idToken();

  if (!token) {
    return { ok: false, reason: FailureReason.Unauthenticated };
  }

  let response: Response;

  try {
    response = await fetch(apiUrl(path), {
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
    return { ok: true, data: (await response.json()) as T };
  }

  if (response.status === 404) {
    return { ok: false, reason: FailureReason.NotFound };
  }

  if (response.status === 401 || response.status === 403) {
    return { ok: false, reason: FailureReason.Unauthenticated };
  }

  return { ok: false, reason: FailureReason.Unexpected, detail: `HTTP ${response.status}` };
}
