# variant-skulabs-details

The admin block on the **product variant** detail page. It looks the variant up in the SkuSync API and
links the merchant straight to the matching SkuLabs item.

Target: `admin.product-variant-details.block.render`. Learn more about admin UI extensions in Shopify's
[developer documentation](https://shopify.dev/docs/apps/build/admin/actions-blocks).

This README also carries the reference material the sibling extensions share — the shared source
layout, how they reach the SkuSync API, environment configuration and local development — because all
three differ only in their target.

This extension was scaffolded as `remote-product-details` and renamed once it stopped being the only
one. Its `uid` came along unchanged, which is what makes that safe: after the first deploy the uid alone
maps a config to an existing extension registration, so a renamed handle updates that registration
rather than deleting it and creating a new one.

## One target per extension is a platform limit

SkuSync has three admin surfaces, and they are three extensions:

| Extension | Target | Surface |
| --- | --- | --- |
| `variant-skulabs-details` | `admin.product-variant-details.block.render` | Card on the variant page — just that variant |
| [`product-skulabs-details`](../product-skulabs-details) | `admin.product-details.block.render` | Card on the product page — every variant |
| [`skulabs-details-action`](../skulabs-details-action) | `admin.product-details.action.render` | **More actions** modal — the only mobile surface |

That is not a stylistic choice. Two separate rules force it, and both are enforced by `shopify app dev`
and `shopify app deploy` — **never by `shopify app build`**, which bundles an illegal target layout
without complaint. A green build proves nothing about whether a target layout is legal.

- **An extension may hold only one of the admin *block* targets.**
  > Extension targeting config should have just one of the following targets
  > [admin.abandoned-checkout-details.block.render, … admin.product-details.block.render,
  > admin.product-variant-details.block.render]

  The CLI's block scaffold says exactly this — "Only 1 target can be specified for each Admin block
  extension" — and it is correct. Don't dismiss it.
- **An extension may not hold a block target and an action target together.**
  > admin.product-details.block.render, admin.product-variant-details.block.render cannot be configured
  > in the same extension as admin.product-details.action.render. Create a new extension with the
  > admin.product-details.block.render, admin.product-variant-details.block.render extension point(s).

  Note the advice in that second message is itself wrong: putting both block targets in one new
  extension trips the first rule. Shopify's
  [admin-extensions reference](https://shopify.dev/docs/api/admin-extensions/latest) also shows a
  block-plus-action example in a single extension. It does not work.

Why the product-page block exists at all, rather than just this one:

- **A single-variant product has no variant page.** Shopify shows such a product as itself and never
  offers the variant view, so a variant-only extension is invisible for exactly the simplest products.
- **A multi-variant product would otherwise cost one page visit per variant.**
- **Inline blocks don't render in the Shopify mobile apps** — that's what the action extension is for.

One upside of being forced apart: the label of a More actions menu item is the extension's `name` from
its locale files, and there is no per-target name. Because the action is its own extension it can be
named "View SkuLabs details" — the wording merchants tap — while both blocks stay named "SkuSync" for
the app-block picker.

## TypeScript

The source is TypeScript (`src/**/*.ts`, `src/**/*.tsx`). The Shopify CLI bundles with
esbuild, which strips types without checking them — a build succeeding says nothing about type
correctness. Run the checker separately:

```sh
pnpm typecheck          # from skusync/, covers every workspace extension
```

`tsc` runs with `strict` on. Three things to know about the setup:

- `tsconfig.json` sets `include` explicitly. Without it `tsc` also walks `dist/` and reports
  thousands of errors from the minified Preact bundle the CLI writes there.
- **`shopify.d.ts` is generated — don't hand-edit it.** The CLI rewrites it on every build with one
  `declare module` block per file it finds under `src/`.
- **`globals.d.ts` is hand-written and declares `shopify` once, globally.** It carries more weight than
  it used to: the generated blocks only cover files under `src/`, so the shared code in
  `skusync/shared` — which is most of the code — gets its `shopify` global from here and nowhere else.
  It also removes an ordering dependency, since the generated blocks only cover files that existed at
  the *last build*: without it, adding a source file makes `tsc` fail until someone runs a build, and a
  CI typecheck with no preceding build fails outright. The two files don't conflict; they populate
  different scopes.
- **Each extension's global is the exact API type of its one target**, which the one-target-per-extension
  limit makes possible. Shared components still can't ask which surface they're on, though — they are
  compiled once per extension against a different global each time — so copy that differs per surface
  (the loading and nothing-to-show messages) is passed in as a prop.

Note that `pnpm typecheck` is not currently run in CI — `.github/workflows/pr-*.yml` has a Node job
for `angular/dashboard` only.

## Layout

Almost nothing lives in these extension directories. Since each surface has to be its own extension, the
view code sits in `skusync/shared` and all three render it — that is what stops the two product surfaces
from drifting apart, and what keeps a third extension from being a third copy. What stays in an
extension is the per-target chrome: read the resource off `data.selected`, wrap a view component in the
surface's wrapper, and nothing else.

```
extensions/variant-skulabs-details/src/targets/VariantBlock.tsx    s-admin-block  + VariantDetails
extensions/product-skulabs-details/src/targets/ProductBlock.tsx   s-admin-block  + ProductVariants
extensions/skulabs-details-action/src/targets/ProductAction.tsx   s-admin-action + ProductVariants

skusync/shared/
  components/
    ProductVariants.tsx        product lookup -> Loading | VariantList | Failure
    VariantDetails.tsx         variant lookup -> Loading | SkulabsLink | Failure
    VariantList.tsx            the rows
    VariantRow.tsx             one row: SKU, display name, link or nothing-to-show
    SkulabsLink.tsx            a link out to a SkuLabs item
    Loading.tsx                spinner plus the message it was given
    Failure.tsx                nothing-found text, or a warning/critical banner
  hooks/
    useLookup.ts               the load/abort state machine, and the LookupState union
    useVariantInformation.ts   useLookup bound to the variant endpoint
    useProductInformation.ts   useLookup bound to the product endpoint
  api/
    result.ts                  the ApiResult union and the FailureReason enum
    request.ts                 the authenticated GET, and status-to-failure mapping
    variantInformation.ts      the variant endpoint and its response type
    productInformation.ts      the product endpoint and its response type
  config/api.ts                the base URL, shared with every other Shopify-side project
```

Three things make `skusync/shared` work, and all three are load-bearing:

- **It is a pnpm workspace package** (`shared/package.json`, listed in `pnpm-workspace.yaml`). Node
  resolution walks up from the importing file, so without `shared/node_modules` the `preact` and
  `@shopify/ui-extensions` imports in that directory resolve to nothing. pnpm symlinks every copy to
  the same store path, so the bundles still contain a single Preact instance.
- **It has its own `tsconfig.json`, for esbuild rather than for `tsc`.** The CLI's esbuild resolves JSX
  per file from the nearest tsconfig; with none there, `.tsx` files in `shared/` default to
  `react/jsx-runtime` and the build fails with "Could not resolve react/jsx-runtime".
- **It has no typecheck script of its own.** Each extension's tsconfig includes `../../shared/**/*`, so
  the shared code is checked once per extension against that extension's real target API. Checking it
  standalone is not possible anyway — which `shopify` global the host injects is a property of the
  extension, not of the directory.

Components read `i18n` off the `shopify` global directly rather than taking it as a prop, but copy that
differs per surface (the loading and nothing-to-show messages) is passed in, because a shared component
cannot ask which target it is rendering in.

Each extension needs its own `locales/*.json` and holds only the keys its own surface uses, so the
strings the product view needs appear in two of them. There is no shared-locale mechanism; the platform
resolves translations per extension. Adding a surface means adding an extension, and the locale strings
are the one thing that has to be copied along with it.

## How it talks to SkuSync

Two endpoints, both authenticated with the Shopify session token from `shopify.auth.idToken()` sent as
a bearer token. The API verifies the token itself (HS256, signed with the app's client secret) — see
`dotnet/src/Web.Api/Shopify/Authentication`.

| Endpoint | Used by | Returns |
| --- | --- | --- |
| `GET /shopify/variant-information?variantId=<gid>` | the variant block | the variant's SkuLabs URL, or 404 |
| `GET /shopify/product-information?productId=<gid>` | `product-skulabs-details` and `skulabs-details-action` | one entry per variant SkuSync holds for the product, each with its SkuLabs URL or `null`, or 404 when it holds none |

The product endpoint lists unlinked variants alongside linked ones so the merchant sees the whole
variant set instead of silently losing the rows that have no SkuLabs item yet. Those rows carry the
same neutral nothing-to-show copy the variant page uses.

The extension does not query the Admin GraphQL API for the variant list, even though direct API access
is enabled on the app. SkuSync already stores every variant it has ingested with its product ID, so one
request to our own API replaces a Shopify query followed by one lookup per variant.

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
grep -o 'function [A-Za-z]*(){try{return"[^"]*"' extensions/*/dist/*.js
```

## Running it locally

The extension sandbox is served over HTTPS, so it cannot call the API over plain HTTP — a
`http://localhost:5257` request is blocked as mixed content. The ngrok tunnel solves this by
terminating TLS with a publicly trusted certificate.

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

## States each surface renders

| State | Rendering |
| --- | --- |
| Loading | Spinner while the lookup is in flight; re-runs when the merchant moves to another product or variant |
| Found | The variant surface links to the SkuLabs item; the product surfaces list every variant with its SKU, display name and link |
| Nothing found (404) | Quiet subdued text — having nothing to show is a normal state, not an error |
| 401 / 403 | Warning banner pointing at the client-secret mismatch |
| Unreachable | Critical banner naming the base URL that didn't respond |

The nothing-found copy says only that there is nothing to display. Whether the resource is unknown to
SkuSync, has no SkuLabs item associated with it, or is soft-deleted is internal state, and neither the
API response nor the extension reveals which applies. The same applies to an unlinked row inside the
product list.

Rows lead with the SKU because that is what SkuLabs is keyed on; the display name sits underneath as
supporting detail, since it repeats the product title on every row of a product.
