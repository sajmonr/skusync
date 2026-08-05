// The Shopify CLI replaces `process.env.<NAME>` at build time via esbuild's `define`, reading the
// `.env` file that matches the active app config (`.env` for `shopify.app.toml`, `.env.production`
// for `shopify app deploy -c production`). Only named members are substituted — reading `process.env`
// as a whole object does not work.
//
// The fallback keeps `shopify app dev` working with no `.env` at all, against the Web.Api `https`
// launch profile. A deployed build MUST set SKUSYNC_API_URL; see this extension's README.
const DEVELOPMENT_API_BASE_URL = "https://localhost:7257";

export const API_BASE_URL = trimTrailingSlash(readConfiguredApiUrl() || DEVELOPMENT_API_BASE_URL);

/**
 * When SKUSYNC_API_URL is set at build time esbuild inlines it here as a string literal. When it
 * isn't, the `process.env` reference survives into the bundle untouched — and `process` doesn't
 * exist in the extension sandbox, so reading it throws. Catching that is what turns a missing
 * variable into the development fallback instead of an extension that fails to load at all.
 */
function readConfiguredApiUrl() {
  try {
    return process.env.SKUSYNC_API_URL;
  } catch {
    return undefined;
  }
}

function trimTrailingSlash(url) {
  return url.endsWith("/") ? url.slice(0, -1) : url;
}
