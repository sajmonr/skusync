import { beforeEach, describe, expect, it, vi } from "vitest";
import { API_BASE_URL } from "../config/api";
import { getFromApi } from "./request";
import { FailureReason } from "./result";

/**
 * The status-to-failure mapping is the whole point of this module, and getting it wrong is invisible
 * until a merchant sees the wrong message: a 404 rendered as a critical banner reads as a broken app
 * rather than as "nothing to show yet".
 */
describe("getFromApi", () => {
  const idToken = vi.fn<() => Promise<string | null>>();

  beforeEach(() => {
    idToken.mockResolvedValue("session-token");
    vi.stubGlobal("shopify", { auth: { idToken } });
  });

  function respondWith(response: Partial<Response>) {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(response as Response);
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock;
  }

  function rejectWith(error: unknown) {
    const fetchMock = vi.fn<typeof fetch>().mockRejectedValue(error);
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock;
  }

  it("returns the parsed body when the request succeeds", async () => {
    respondWith({ ok: true, status: 200, json: async () => ({ variantId: 42 }) });

    const result = await getFromApi<{ variantId: number }>("shopify/variant-information");

    expect(result).toEqual({ ok: true, data: { variantId: 42 } });
  });

  it("sends the session token as a bearer token against the configured base URL", async () => {
    const fetchMock = respondWith({ ok: true, status: 200, json: async () => ({}) });

    await getFromApi("shopify/product-information?productId=1");

    expect(fetchMock).toHaveBeenCalledWith(
      `${API_BASE_URL}/shopify/product-information?productId=1`,
      expect.objectContaining({
        method: "GET",
        headers: { Authorization: "Bearer session-token", Accept: "application/json" },
      }),
    );
  });

  it("reports unauthenticated without calling the API when there is no session token", async () => {
    idToken.mockResolvedValue(null);
    const fetchMock = respondWith({ ok: true, status: 200, json: async () => ({}) });

    const result = await getFromApi("shopify/variant-information");

    expect(result).toEqual({ ok: false, reason: FailureReason.Unauthenticated });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    [404, FailureReason.NotFound],
    [401, FailureReason.Unauthenticated],
    [403, FailureReason.Unauthenticated],
  ])("maps HTTP %i onto %s", async (status, reason) => {
    respondWith({ ok: false, status });

    const result = await getFromApi("shopify/variant-information");

    expect(result).toEqual({ ok: false, reason });
  });

  it.each([400, 418, 500, 503])("maps an unhandled HTTP %i onto unexpected, naming the status", async (status) => {
    respondWith({ ok: false, status });

    const result = await getFromApi("shopify/variant-information");

    expect(result).toEqual({
      ok: false,
      reason: FailureReason.Unexpected,
      detail: `HTTP ${status}`,
    });
  });

  it("reports unreachable and names the base URL when the transport fails", async () => {
    rejectWith(new TypeError("Failed to fetch"));

    const result = await getFromApi("shopify/variant-information");

    expect(result).toEqual({
      ok: false,
      reason: FailureReason.Unreachable,
      detail: API_BASE_URL,
    });
  });

  it("rethrows an abort instead of reporting it as a failure", async () => {
    // A merchant switching variants aborts the in-flight lookup. Swallowing it here would repaint the
    // block as an error on every navigation, so callers have to be able to tell an abort apart.
    const abort = new Error("The operation was aborted");
    abort.name = "AbortError";
    rejectWith(abort);

    await expect(getFromApi("shopify/variant-information")).rejects.toThrow(abort);
  });

  it("passes the caller's abort signal through to fetch", async () => {
    const fetchMock = respondWith({ ok: true, status: 200, json: async () => ({}) });
    const controller = new AbortController();

    await getFromApi("shopify/variant-information", controller.signal);

    expect(fetchMock.mock.calls[0]?.[1]?.signal).toBe(controller.signal);
  });
});
