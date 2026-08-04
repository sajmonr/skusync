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
