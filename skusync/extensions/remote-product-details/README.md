# remote-product-details

An admin block extension on the product variant detail page. It looks the variant up in the SkuSync
API and links the merchant straight to the matching SkuLabs item.

Target: `admin.product-variant-details.block.render`. Learn more about admin block extensions in
Shopify's [developer documentation](https://shopify.dev/docs/apps/admin/admin-actions-and-blocks).

## TypeScript

The source is TypeScript (`src/*.ts`, `src/BlockExtension.tsx`). The Shopify CLI bundles with
esbuild, which strips types without checking them — a build succeeding says nothing about type
correctness. Run the checker separately:

```sh
pnpm typecheck          # from skusync/, covers every workspace extension
```

`tsc` runs with `strict` on. Three things to know about the setup:

- `tsconfig.json` sets `include` explicitly. Without it `tsc` also walks `dist/` and reports
  thousands of errors from the minified Preact bundle the CLI writes there.
- **`shopify.d.ts` is generated — don't hand-edit it.** The CLI rewrites it on every build with one
  `declare module` block per file it finds under `src/`. Those blocks are what bring the `shopify`
  global into scope, so they are load-bearing, not decorative.
- **`globals.d.ts` is hand-written and declares `shopify` once, globally.** It exists because the
  generated per-file blocks only cover files that existed at the *last build* — without it, adding a
  source file makes `tsc` fail until someone runs a build, and a CI typecheck with no preceding build
  fails outright. The two files don't conflict; they populate different scopes.

Note that `pnpm typecheck` is not currently run in CI — `.github/workflows/pr-*.yml` has a Node job
for `angular/dashboard` only.

## Layout

One component per file:

```
src/
  BlockExtension.tsx           entry point + the Extension block itself
  components/
    Loading.tsx                spinner shown while the lookup is in flight
    SkulabsLink.tsx            the link + item ID, on success
    Failure.tsx                not-linked text, or a warning/critical banner
  hooks/
    useVariantInformation.ts   the lookup, and the LookupState union it returns
  api.ts                       the fetch call and its typed result union
  config.ts                    API base URL resolution
```

Child components read `i18n` (and `extension.target`) off the `shopify` global directly rather than
taking them as props.

## How it talks to SkuSync

The extension calls `GET /shopify/variant-information?variantId=<gid>` on the SkuSync API, passing
the Shopify session token from `shopify.auth.idToken()` as a bearer token. The API verifies that
token itself (HS256, signed with the app's client secret) — see
`dotnet/src/Web.Api/Shopify/Authentication`.

Three pieces have to line up or the request never completes:

| Requirement | Where it lives |
| --- | --- |
| `network_access = true` capability | `shopify.extension.toml` — without it the sandbox blocks the fetch before it is sent |
| CORS for `https://extensions.shopifycdn.com` | `ShopifyExtensionCors` in Web.Api; extensions run from that origin in every environment |
| Matching client secret | `Shopify:App:ClientSecret` in the API's configuration |

## Configuring the API base URL

`src/config.ts` reads `process.env.SKUSYNC_API_URL`, which the Shopify CLI substitutes at build time
from the `.env` file matching the active app config — `.env` for `shopify.app.toml`, `.env.<name>`
for `shopify app deploy -c <name>`. Only named members are substituted; reading `process.env` as an
object does not work.

When the variable is unset the reference survives into the bundle, and `process` doesn't exist in the
extension sandbox — so `config.ts` reads it inside a `try`/`catch` and falls back to
`https://localhost:7257` for local development. **A deployed build must set `SKUSYNC_API_URL`**, or
it will silently ship pointing at localhost. `.env*` is gitignored, so these files are created per
machine and per deploy environment:

```sh
# .env — used by `shopify app dev`
SKUSYNC_API_URL=https://localhost:7257
```

Verify what a build actually baked in:

```sh
shopify app build
grep -o 'https://[^"]*' extensions/remote-product-details/dist/remote-product-details.js
```

## Running it locally

The extension sandbox is served over HTTPS, so it cannot call the API over plain HTTP — a
`http://localhost:5257` request is blocked as mixed content. Use the `https` launch profile, which
listens on `https://localhost:7257` alongside the existing HTTP port:

```sh
# once, so the browser trusts the local certificate
dotnet dev-certs https --trust

docker compose up -d                                      # Postgres
cd dotnet && dotnet run --project src/Web.Api --launch-profile https
```

The API needs the app's credentials to verify session tokens. They are secrets, so they go in user
secrets rather than `appsettings.Development.json`:

```sh
cd dotnet/src/Web.Api
dotnet user-secrets set "Shopify:App:ClientId" "<client id from shopify.app.toml>"
dotnet user-secrets set "Shopify:App:ClientSecret" "<client secret from the Partner dashboard>"
dotnet user-secrets set "Shopify:ShopUrl" "https://<your-dev-store>.myshopify.com"
```

`Shopify:ShopUrl` matters: the API rejects any token whose `dest` claim names a different shop, and
the committed development value is a placeholder. Without these the API logs a warning at startup
and every Shopify endpoint answers 401.

Then, from `skusync/`:

```sh
shopify app dev
```

If the sandbox rejects the local development certificate, put a trusted HTTPS hostname in front of
the API instead and point `SKUSYNC_API_URL` at it:

```sh
cloudflared tunnel --url http://localhost:5257
```

## States the block renders

| State | Rendering |
| --- | --- |
| Loading | Spinner while the lookup is in flight; re-runs when the merchant switches variants |
| Found | A link to the SkuLabs item plus its item ID |
| Nothing found (404) | Quiet subdued text — having nothing to show is a normal state, not an error |
| 401 / 403 | Warning banner pointing at the client-secret mismatch |
| Unreachable | Critical banner naming the base URL that didn't respond |

The 404 copy says only that there is nothing to display. Whether the variant is unknown to SkuSync,
has no SkuLabs item associated with it, or is soft-deleted is internal state, and neither the API
response nor the extension reveals which applies.
