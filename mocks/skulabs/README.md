# Local SkuLabs mock (WireMock)

A local stand-in for the SkuLabs Items API, run as part of the dev stack
(`docker compose up -d`) via the `skulabs-mock` service. Uses
[`holomekc/wiremock-gui`](https://github.com/holomekc/wiremock) — WireMock with an
integrated web UI.

- **Mock base URL:** `http://localhost:5675` — `Skulabs:Api:BaseUrl` in
  `appsettings.Development.json` already points here.
- **Web UI:** http://localhost:5675/__admin/webapp/ — create/edit/toggle stubs live.
- **Admin API:** `http://localhost:5675/__admin` — same control surface, scriptable.

## Committed defaults (loaded on startup)

The `mappings/` + `__files/` here are the baseline, mounted read-only, so the committed
defaults can never be mutated by the running server. Live edits (UI or admin API) stay in
the server's memory; **restart the container to reset to these defaults**
(`docker compose restart skulabs-mock`).

| Stub | Behaviour |
|---|---|
| `item-get.json` | `GET /item/get` with `Authorization: Bearer skusync-local-mock-key` → 200, body from `__files/skulabs-items.json` |
| `item-bulk-upsert.json` | `PUT /item/bulk_upsert` with the same header → 200 `{"items":[]}` |
| `unauthorized.json` | any `/item/*` request without that header → 401 (low priority fallback) |

The dummy key `skusync-local-mock-key` matches `Skulabs:Api:ApiKey` in dev appsettings.

Most items in `__files/skulabs-items.json` carry `alias_locations`, the per-warehouse bin
location map, so locations are visible throughout the dev stack. Bin labels normally look
like `B-06-06` — a letter, then two two-digit numbers.

Some entries deliberately break that pattern, keeping the absent and messy paths exercised
locally rather than only in tests:

| Item | `alias_locations` | Mirrors as |
|---|---|---|
| `Gift Card ($25)`, both `Phantom Item`s | key absent entirely | `""` |
| `The Multi-location Snowboard` | present, but only warehouse `7991…a419` | `""` |
| `The Untracked Snowboard` | `A-01-6` — last part not padded | verbatim |
| `The Videographer Snowboard` | `c-12-03` — lowercase aisle | verbatim |
| `The Collection Snowboard: Liquid (Large / Red)` | `B-3-07` — middle part not padded | verbatim |
| `Ambiguous Multi-Variant Item` | `AA-02-11` — two-letter aisle | verbatim |

The off-format values are real-world noise, not fixtures to clean up. Nothing parses or
validates a location: SkuLabs owns the value and we mirror it exactly as sent.

The warehouse whose location the app mirrors is `Skulabs:Api:WarehouseId`
(`69912a8923657b958806a418` in dev appsettings). Blank turns the feature off: the app stops
requesting `alias_locations` and leaves any locations it already stored untouched.

## Testing other scenarios at runtime

Edit stubs in the web UI, or drive the admin API. Examples:

```bash
# Force the rate-limit path (429 + Retry-After) — the app records a cooldown and defers
curl -sX POST localhost:5675/__admin/mappings -d '{
  "priority": 0,
  "request":  { "method": "GET", "urlPath": "/item/get" },
  "response": { "status": 429, "headers": { "Retry-After": "120" } }
}'

# Inspect what the app actually sent SkuLabs (the request journal)
curl -s localhost:5675/__admin/requests | jq '.requests[].request | {method, url}'

# Reset in-memory stubs back to the committed defaults on disk
curl -sX POST localhost:5675/__admin/mappings/reset
```
