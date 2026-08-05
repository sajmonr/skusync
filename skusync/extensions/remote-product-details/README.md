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
    Failure.tsx                nothing-found text, or a warning/critical banner
  hooks/
    useVariantInformation.ts   the lookup, and the LookupState union it returns
  api.ts                       the fetch call and its typed result union
```

The API base URL is not resolved here — it lives in `skusync/shared/config/api.ts` so other
Shopify-side projects resolve it identically.

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

URL construction lives in **`skusync/shared/config/api.ts`**, not in this extension, so other
Shopify-side projects resolve it the same way. It maps an environment to an origin:

| `NODE_ENV` | API base URL |
| --- | --- |
| `development` | `https://shopify-skusync.ngrok.app` (the fixed tunnel; must match `ngrok.yml`) |
| `production` | `https://skusync.darkflux.app` |

It is resolved at **build** time, not runtime: the Shopify CLI substitutes `process.env.NODE_ENV` into
the bundle via esbuild's `define`, and Shopify's CDN serves that bundle verbatim — there is no runtime
configuration on Shopify's side. Whatever environment is set when the build runs is baked in until the
next deploy. Only named members are substituted; reading `process.env` as an object does not work.

**Every package script sets `NODE_ENV` explicitly**, and that is deliberate:

```sh
pnpm dev                # NODE_ENV=development -> tunnel URL
pnpm build              # NODE_ENV=development -> tunnel URL
pnpm build:production    # NODE_ENV=production  -> skusync.darkflux.app
pnpm deploy             # NODE_ENV=production  -> skusync.darkflux.app
```

The reason nothing relies on the default: **the CLI sets `NODE_ENV=production` itself when the variable
is unset.** So bypassing these scripts (a bare `shopify app build`) produces a *production* bundle,
not a development one. That is the safer of the two failure modes — an unlabelled build points at
production rather than at someone's dev tunnel — but it is the opposite of what an unset variable
normally implies, so it is worth knowing. Note also that the match is exact: `NODE_ENV=Production`
resolves to development, which is why the scripts hardcode the value rather than leaving it to a
typo-prone shell.

Verify what a build actually baked in:

```sh
grep -o 'function [A-Za-z]*(){try{return"[^"]*"' \
  extensions/remote-product-details/dist/remote-product-details.js
```

## Running it locally

The extension sandbox is served over HTTPS, so it cannot call the API over plain HTTP — a
`http://localhost:5257` request is blocked as mixed content. The ngrok tunnel solves this by
terminating TLS with a publicly trusted certificate:

The whole stack, including the tunnel and the CLI dev session, comes up from the repo root:

```sh
docker compose up -d                                   # Postgres
process-compose -f process-compose.yaml --no-server up # everything else
```

That runs `shopify-app-install` (`pnpm install`, a no-op when already satisfied), `shopify-tunnel`
and `shopify-app-dev` alongside the .NET hosts and the dashboard. **Run `shopify app dev` by hand
once first** — the CLI needs a terminal for login and store selection before it can start unattended.
ngrok's request log is at <http://127.0.0.1:4040>, which is where the per-request detail lives since
ngrok prints nothing without a terminal of its own.

`shopify-app-dev` sets `CI=1`, which turns off the CLI's live terminal UI. Without it the dev session
repaints in place using cursor-control escapes, which a log pane renders as garbage instead of
scrolling lines. CI mode also makes the CLI fail on a prompt rather than wait for input nobody can
give. If it ever refuses to run without a terminal, add `is_tty: true` to that process and accept the
noisier output.

To work on just the Shopify side, `cd skusync && pnpm dev` runs the tunnel and the CLI together via
`concurrently --kill-others`, so quitting either tears down both. Because the tunnel hostname is
fixed there is nothing to wait for and nothing to discover — the two are independent processes,
which is also why process-compose can manage them separately rather than nesting `concurrently`
inside it.

Two things to know:

- **The hostname is a custom ngrok subdomain, which is a paid feature.** It lives in `ngrok.yml` as
  the endpoint's `url:`. `ngrok.io` is the legacy domain; current accounts use `ngrok.app`.
- **`--config` replaces the agent's default config rather than adding to it.** The `tunnel` script
  names the global config (holding your authtoken) alongside `ngrok.yml`; passing only `ngrok.yml`
  fails with `ERR_NGROK_4018`, "not authenticated", which reads misleadingly like a bad token. The
  script hardcodes the macOS config path — set `NGROK_CONFIG` to override it elsewhere.

`pnpm tunnel` runs the tunnel alone. `pnpm dev:no-tunnel` skips it — useful when the tunnel is already
running in another terminal, though note the extension still builds against the tunnel hostname, so
something has to be serving it.

There is no longer an HTTPS launch profile on Web.Api. It existed to work around the mixed-content
block before the tunnel did, and its self-signed certificate was the weaker option anyway — the
sandbox may reject it, where ngrok's certificate is publicly trusted. Web.Api serves plain HTTP on
:5257 and the tunnel terminates TLS in front of it.

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
