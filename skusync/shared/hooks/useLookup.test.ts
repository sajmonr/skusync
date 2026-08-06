// @vitest-environment jsdom

import { h, render } from "preact";
import { act } from "preact/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import { type ApiResult, FailureReason } from "../api/result";
import { type Lookup, type LookupState, useLookup } from "./useLookup";

/**
 * The abort behaviour is what these cover. A merchant clicking through variants leaves a trail of
 * in-flight requests, and if a stale one is allowed to land it overwrites newer state or repaints the
 * block as an error — both of which look like the extension is broken rather than busy.
 *
 * The probe is built with `h()` rather than JSX so the suite needs no transform.
 */
describe("useLookup", () => {
  let container: HTMLDivElement | undefined;

  afterEach(() => {
    if (container) {
      render(null, container);
      container.remove();
      container = undefined;
    }
  });

  /** Renders the hook and returns a handle for reading its latest state and changing the gid. */
  function renderLookup<T>(gid: string | undefined, lookup: Lookup<T>) {
    container = document.createElement("div");
    document.body.append(container);

    const states: LookupState<T>[] = [];

    function Probe({ resourceGid }: { resourceGid: string | undefined }) {
      states.push(useLookup(resourceGid, lookup));
      return null;
    }

    const mount = (resourceGid: string | undefined) =>
      act(() => {
        render(h(Probe, { resourceGid }), container!);
      });

    mount(gid);

    return {
      current: () => states[states.length - 1]!,
      setGid: mount,
    };
  }

  /**
   * Settles a lookup and drains everything that follows.
   *
   * A rejection needs one microtask more than a resolution — it passes through the skipped `.then`
   * before reaching `.catch` — and `act` alone doesn't wait that long. Draining via a macrotask covers
   * both, and without it a test asserting "state didn't change" would pass whether the handler ran or
   * not.
   */
  async function settled(action: () => void) {
    await act(async () => {
      action();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }

  /** A lookup whose promise the test resolves by hand, so timing is explicit rather than raced. */
  function deferredLookup<T>() {
    const calls: { gid: string; signal: AbortSignal; settle: (result: ApiResult<T>) => void; fail: (error: unknown) => void }[] = [];

    const lookup: Lookup<T> = (gid, signal) =>
      new Promise<ApiResult<T>>((resolve, reject) => {
        calls.push({ gid, signal, settle: resolve, fail: reject });
      });

    return { lookup, calls };
  }

  it("starts in loading", () => {
    const { lookup } = deferredLookup<string>();

    const probe = renderLookup("gid://shopify/Product/1", lookup);

    expect(probe.current()).toEqual({ status: "loading" });
  });

  it("does not look anything up until a gid arrives", () => {
    // The render targets hand over undefined for the tick before the host supplies their selection.
    const { lookup, calls } = deferredLookup<string>();

    const probe = renderLookup(undefined, lookup);

    expect(calls).toHaveLength(0);
    expect(probe.current()).toEqual({ status: "loading" });
  });

  it("moves to loaded with the data", async () => {
    const { lookup, calls } = deferredLookup<{ productId: number }>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    await settled(() => calls[0]!.settle({ ok: true, data: { productId: 1 } }));

    expect(probe.current()).toEqual({ status: "loaded", data: { productId: 1 } });
  });

  it("moves to failed carrying the failure", async () => {
    const { lookup, calls } = deferredLookup<string>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    await settled(() => calls[0]!.settle({ ok: false, reason: FailureReason.NotFound }));

    expect(probe.current()).toEqual({
      status: "failed",
      failure: { ok: false, reason: FailureReason.NotFound },
    });
  });

  it("aborts the in-flight lookup and re-runs when the gid changes", async () => {
    const { lookup, calls } = deferredLookup<string>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    expect(calls[0]!.signal.aborted).toBe(false);

    await act(async () => probe.setGid("gid://shopify/Product/2"));

    expect(calls[0]!.signal.aborted).toBe(true);
    expect(calls).toHaveLength(2);
    expect(calls[1]!.gid).toBe("gid://shopify/Product/2");
    expect(probe.current()).toEqual({ status: "loading" });
  });

  it("ignores an abort rejection rather than rendering it as an error", async () => {
    const { lookup, calls } = deferredLookup<string>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    const abort = new Error("The operation was aborted");
    abort.name = "AbortError";
    await settled(() => calls[0]!.fail(abort));

    expect(probe.current()).toEqual({ status: "loading" });
  });

  it("reports an unexpected throw with its message", async () => {
    const { lookup, calls } = deferredLookup<string>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    await settled(() => calls[0]!.fail(new Error("boom")));

    expect(probe.current()).toEqual({
      status: "failed",
      failure: { ok: false, reason: FailureReason.Unexpected, detail: "boom" },
    });
  });

  it("does not re-run for an unchanged gid on re-render", async () => {
    const { lookup, calls } = deferredLookup<string>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    await act(async () => probe.setGid("gid://shopify/Product/1"));

    expect(calls).toHaveLength(1);
  });

  it("aborts on unmount", () => {
    const { lookup, calls } = deferredLookup<string>();
    renderLookup("gid://shopify/Product/1", lookup);

    act(() => render(null, container!));

    expect(calls[0]!.signal.aborted).toBe(true);
  });

  it("keeps a stale result from overwriting a newer one", async () => {
    // Aborting is advisory — a lookup that ignores the signal still settles. Without the `current`
    // guard in the effect, this late resolution would replace the newer variant's data with the one
    // the merchant has already navigated away from.
    const { lookup, calls } = deferredLookup<string>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    await act(async () => probe.setGid("gid://shopify/Product/2"));
    await settled(() => calls[1]!.settle({ ok: true, data: "newer" }));
    await settled(() => calls[0]!.settle({ ok: true, data: "stale" }));

    expect(probe.current()).toEqual({ status: "loaded", data: "newer" });
  });

  it("ignores a stale failure too", async () => {
    const { lookup, calls } = deferredLookup<string>();
    const probe = renderLookup("gid://shopify/Product/1", lookup);

    await act(async () => probe.setGid("gid://shopify/Product/2"));
    await settled(() => calls[1]!.settle({ ok: true, data: "newer" }));
    await settled(() => calls[0]!.fail(new Error("stale transport failure")));

    expect(probe.current()).toEqual({ status: "loaded", data: "newer" });
  });
});
