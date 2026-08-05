// The SkuSync API origin the extension calls.
//
// This value is resolved at BUILD time, not runtime: the Shopify CLI substitutes
// `process.env.SKUSYNC_API_URL` into the bundle via esbuild's `define`, and Shopify's CDN then serves
// that bundle verbatim. There is no runtime configuration on Shopify's side, so whatever is baked in
// at `shopify app deploy` time is what every merchant's browser calls until the next deploy.
//
// Development needs no configuration at all — it falls back to the fixed ngrok hostname that
// `pnpm dev` opens a tunnel on (see ngrok.yml at the repo root; the two must agree).
//
// Production overrides it at deploy time:
//
//   SKUSYNC_API_URL=https://api.example.com shopify app deploy
//
// Forgetting that ships the development tunnel URL to production, since the fallback below cannot
// know which it is building for.
const DEVELOPMENT_API_BASE_URL = "https://shopify-skusync.ngrok.app";

// Declared here rather than pulled in from @types/node: nothing in this extension runs on Node, and
// this is the only Node global the build substitutes. The declaration is erased at compile time, so
// it does not promise `process` exists at runtime — readConfiguredApiUrl handles it not existing.
declare const process: { env: Record<string, string | undefined> };

export const API_BASE_URL: string = trimTrailingSlash(
  readConfiguredApiUrl() || DEVELOPMENT_API_BASE_URL,
);

/**
 * When SKUSYNC_API_URL is set at build time esbuild inlines it here as a string literal. When it
 * isn't, the `process.env` reference survives into the bundle untouched — and `process` doesn't exist
 * in the extension sandbox, so reading it throws. Catching that is what turns a missing variable into
 * the development fallback instead of an extension that fails to load at all.
 */
function readConfiguredApiUrl(): string | undefined {
  try {
    return process.env.SKUSYNC_API_URL?.trim();
  } catch {
    return undefined;
  }
}

function trimTrailingSlash(url: string): string {
  return url.endsWith("/") ? url.slice(0, -1) : url;
}
