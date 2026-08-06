import { beforeEach, describe, expect, it, vi } from "vitest";
import { fetchProductInformation, type ProductInformation } from "./productInformation";
import { FailureReason } from "./result";

describe("fetchProductInformation", () => {
  beforeEach(() => {
    vi.stubGlobal("shopify", { auth: { idToken: async () => "session-token" } });
  });

  function respondWith(status: number, body?: ProductInformation) {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: async () => body,
    } as Response);
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock;
  }

  const variant = {
    variantId: 987654321,
    sku: "ABC-1",
    title: "Basic Tee (Small / Red)",
    skulabsUrl: "https://app.skulabs.com/item?id=sl-1",
  };

  it("returns the variants a product has", async () => {
    respondWith(200, { productId: 555, variants: [variant] });

    const result = await fetchProductInformation("gid://shopify/Product/555");

    expect(result).toEqual({ ok: true, data: { productId: 555, variants: [variant] } });
  });

  it("folds an empty variant list into not-found", async () => {
    // Otherwise every surface would render an empty card. The API answers 404 when it holds nothing for
    // a product, so this only covers a body that arrives empty anyway — but the components are written
    // to trust that a loaded state has something in it.
    respondWith(200, { productId: 555, variants: [] });

    const result = await fetchProductInformation("gid://shopify/Product/555");

    expect(result).toEqual({ ok: false, reason: FailureReason.NotFound });
  });

  it("keeps a variant that has no SkuLabs item", async () => {
    const unlinked = { ...variant, skulabsUrl: null };
    respondWith(200, { productId: 555, variants: [unlinked] });

    const result = await fetchProductInformation("gid://shopify/Product/555");

    expect(result).toEqual({ ok: true, data: { productId: 555, variants: [unlinked] } });
  });

  it("percent-encodes the product's global ID into the query string", async () => {
    const fetchMock = respondWith(200, { productId: 555, variants: [variant] });

    await fetchProductInformation("gid://shopify/Product/555");

    expect(fetchMock.mock.calls[0]?.[0]).toContain(
      "productId=gid%3A%2F%2Fshopify%2FProduct%2F555",
    );
  });

  it("passes a failure through untouched", async () => {
    respondWith(503);

    const result = await fetchProductInformation("gid://shopify/Product/555");

    expect(result).toEqual({
      ok: false,
      reason: FailureReason.Unexpected,
      detail: "HTTP 503",
    });
  });
});
