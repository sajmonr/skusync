import { defineConfig } from "vitest/config";

// Only the shared package has tests: it holds the request/mapping/state logic all three extensions
// render, and it is the part that can be exercised without a Shopify host at all.
//
// There is deliberately no JSX or Preact plugin here. The suite covers plain TypeScript, and the one
// hook test builds its probe component with `h()` rather than JSX, so nothing needs transforming.
// Component-level rendering assertions would need @shopify/ui-extensions-tester, which is a separate
// decision — see the extension README.
export default defineConfig({
  test: {
    // Node by default; the hook test opts into jsdom with a `@vitest-environment` docblock, so the
    // fast majority of the suite doesn't pay for a DOM.
    environment: "node",
    include: ["**/*.test.ts"],
    restoreMocks: true,
    unstubGlobals: true,
  },
});
