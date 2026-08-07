// Where the SkuSync API lives, per environment. Shared so every extension and future Shopify-side
// project resolves the same URL the same way instead of each hardcoding its own copy.
//
// This resolves at BUILD time, not runtime. The Shopify CLI substitutes `process.env.<NAME>` into the
// bundle via esbuild's `define`, and Shopify's CDN then serves that bundle verbatim — there is no
// runtime configuration on Shopify's side. Whatever environment is set when `shopify app build` /
// `shopify app deploy` runs is baked in until the next deploy.

/** The deployments the Shopify-side projects are built for. */
export type AppEnvironment = "development" | "production";

const API_BASE_URLS: Record<AppEnvironment, string> = {
  // The fixed ngrok tunnel that fronts the local Web.Api host. Must match the endpoint's `url:` in
  // ngrok.yaml at the repo root.
  development: "https://shopify-skusync.ngrok.app",
  production: "https://api.skusync.darkflux.app",
};

// Declared here rather than pulled in from @types/node: nothing in these projects runs on Node, and
// this is the only Node global the build substitutes. The declaration is erased at compile time, so
// it does not promise `process` exists at runtime — resolveAppEnvironment handles it not existing.
declare const process: { env: Record<string, string | undefined> };

/**
 * Resolves the environment from `NODE_ENV`, treating anything other than an explicit `production` —
 * including an unset value — as development.
 *
 * When NODE_ENV is set at build time esbuild inlines it here as a string literal and folds the
 * comparison away. When it isn't, the `process.env` reference survives into the bundle untouched, and
 * `process` doesn't exist in the extension sandbox, so reading it throws. Catching that is what makes
 * an unset value fall through to development rather than failing the whole extension on load.
 */
export function resolveAppEnvironment(): AppEnvironment {
  try {
    return process.env.NODE_ENV === "production" ? "production" : "development";
  } catch {
    return "development";
  }
}

export const APP_ENVIRONMENT: AppEnvironment = resolveAppEnvironment();

/** The origin of the SkuSync API for the environment this bundle was built for. */
export const API_BASE_URL: string = API_BASE_URLS[APP_ENVIRONMENT];

/**
 * Joins `path` onto {@link API_BASE_URL}, tolerating a leading slash so callers don't have to agree
 * on whether to include one.
 */
export function apiUrl(path: string): string {
  return `${API_BASE_URL}/${path.replace(/^\/+/, "")}`;
}
