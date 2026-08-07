# SkuSync Sync-Pipeline Architecture

> **Status: target architecture.** Implemented by [#91](https://github.com/sajmonr/skusync/issues/91);
> until that lands, the code still follows the earlier event-based design. Remove this banner on completion.

SkuSync bridges Shopify and SkuLabs. Postgres holds a mirror of each system; **Ingest**
absorbs external state, **Reconcile** is the only thing that creates divergence (dirty rows),
**Dispatch** is the only thing that writes it back. Everything else — the dashboard, manual
sync, feature flags, retry — hangs off those three stages.

```
                INGEST                      RECONCILE                       DISPATCH
 Shopify ──webhooks(SQS)──┐         ┌──────────────────────┐        ┌─ ShopifyDispatcher ──► Shopify
 Shopify ──import(job)────┼► mirror │ authority rules       │ dirty  │   (batch per product,
                          │  rows   │  SkuLabs → sku/barcode│ bits ──┤    retry, deactivate at 3)
 SkuLabs ──item sync(job)─┘         │  Variant  → title     │        └─ SkulabsDispatcher ──► SkuLabs
                                    │  missing → generate SKU│            (one bulk_upsert,
                                    └──────────────────────┘             rate-limit aware)

        Postgres = data + dirty queue + Hangfire.  SQS = inbound webhooks.  No message broker.
```

## Principles

1. **Postgres is the hub and the only internal coordination layer** (data, dirty-state queue,
   Hangfire job storage). SQS remains the durable inbound queue for Shopify webhooks.
2. **Every field has one authoritative source.** SkuLabs owns SKU and barcode (a blank SkuLabs
   value is never authoritative). The Shopify variant `DisplayName` owns the SkuLabs item title.
   Single-writer-per-field removes bidirectional merge conflicts by construction.
3. **Correctness never depends on messages or real-time paths.** Delete every trigger except the
   scheduled reconcile + dispatchers and the system still converges, just slower. Real-time
   behaviour is a latency optimization layered on top.
4. **Dirty state is both the work queue and the UI status.** The Item Sync grid's pending
   column and the dispatchers' input are the same query.
5. **Manual and automatic paths share the same machinery**, differing only at the trigger.

## Stage 1 — Ingest (absorb external state)

| Component | Trigger | Responsibility |
|---|---|---|
| Shopify webhook handlers (create / update / delete) | SQS | Mirror Shopify's product/variant state locally: create, update, mark deleted, reactivate. |
| Shopify product import (`ProductsService.SyncProducts`) | daily job + full sync | Same, full catalogue. |
| SkuLabs item sync | 10-minute job + full sync | Pull SkuLabs items and the Shopify listings they report, link/relink to variants. An item with several listings is ambiguous, which follows from its listing count rather than being recorded anywhere. |

Rules:

- Ingest mirrors external state. It performs no cross-mirror comparison and no external writes.
- **Exception — SKU origination.** A variant arriving without a SKU gets one generated
  synchronously (`SkuGenerator`), inside the same transaction as the mirror write, so a variant
  row is never visible without a SKU. Because generation creates a divergence from Shopify, the
  writer sets `PendingShopifySync = true` at that moment and, after commit, triggers an
  **immediate scoped dispatch** (see Stage 3) so the SKU reaches Shopify within seconds.
- Each ingest operation ends by invoking Reconcile for the touched scope (plain in-process call).

## Stage 2 — Reconcile (the origination point of divergence)

Pure local computation. No feature flags, no external calls. Invoked per-scope by ingest and as
a nightly full-catalogue sweep (the safety net).

| Rule | Action |
|---|---|
| Linked item SKU/barcode ≠ variant (blank never authoritative) | Mirror SkuLabs values into the variant → variant dirty |
| Variant `DisplayName` ≠ item `Title` | Mirror display name into the item → item dirty |

Every correction writes a `VariantLogMessages` audit event so the change is visible in the
variant history.

## Stage 3 — Dispatch (the only external writers)

| | `ShopifyDispatcher` | `SkulabsDispatcher` |
|---|---|---|
| Drains | `PendingShopifySync` variants (active, not deleted) | `PendingSkulabsSync` items (linked, active) |
| Batch shape | Group by product → one GraphQL mutation per product | Single `bulk_upsert` for the batch |
| On success | Clear bit, reset `FailedShopifySyncAttempts` | Clear bit, reset `FailedSkulabsSyncAttempts` |
| On failure | Keep bit, increment counter, deactivate variant at 3 (+ audit event) | Keep bit, increment counter (all items in the failed batch), exclude item at 3 (+ audit event) |
| Rate limit | n/a | `RateLimitedException` stops the run; rows stay dirty, **counters untouched** — rate-limiting is "later", not "broken" |
| Scheduled cadence | ~2 min | ~5 min |

Notes:

- A failed or suppressed push needs no bookkeeping beyond the counter: the row stays dirty and
  the next run retries. Retry, auto-off catch-up, and monitoring all fall out of the queue shape.
- Dispatch is drain-dirty + clear-on-success, so overlapping runs (scheduled, immediate, manual)
  at worst push the same values twice — idempotent at the target's end.
- **Immediate dispatch:** ingest triggers a scoped dispatcher run right after committing a
  generated SKU (webhooks: per product; import: at product boundaries while it walks the
  catalogue). Best-effort — commit first, push after; on failure log and leave dirty for the
  scheduled run. SQS redelivery therefore only ever reflects *ingest* failures, never target
  outages.
- Future knob (not built): batch bisection in `SkulabsDispatcher` if one poisonous item ever
  drags healthy batch-mates toward the exclusion threshold.

## State model

| Field | Meaning | Set by | Cleared by |
|---|---|---|---|
| `ShopifyProductVariant.PendingShopifySync` | Variant diverges from Shopify | Ingest (SKU generation), Reconcile | `ShopifyDispatcher` on confirmed success |
| `SkulabsItem.PendingSkulabsSync` | Item diverges from SkuLabs | Reconcile | `SkulabsDispatcher` on confirmed success |
| `ShopifyProductVariant.FailedShopifySyncAttempts` / `IsActive` | Consecutive push failures / exclusion | `ShopifyDispatcher` | `ShopifyDispatcher` |
| `SkulabsItem.FailedSkulabsSyncAttempts` | Consecutive push failures / exclusion at 3 | `SkulabsDispatcher` | `SkulabsDispatcher` |

## Feature flags

| Flag | Gates | Checked in |
|---|---|---|
| `ShopifySyncEnabled` | Shopify ingest (webhooks) | webhook handlers |
| `SkulabsSyncEnabled` | SkuLabs ingest (item sync) | item sync job |
| `ShopifyAutoDispatch` *(new, default on)* | scheduled + immediate Shopify dispatch | dispatch triggers |
| `SkulabsAutoDispatch` *(new, default on)* | scheduled SkuLabs dispatch | dispatch trigger |
| `ShopifyWriteBack` | **kill switch** — every Shopify write, incl. manual | `ShopifyDispatcher` push method |
| `SkulabsWriteBack` | **kill switch** — every SkuLabs write, incl. manual | `SkulabsDispatcher` push method |

Manual-only mode = kill switch ON + auto OFF: reconcile keeps marking dirty, the grid shows the
queue, and only the manual button pushes.

## Scheduled jobs

| Job | Cadence | Stage |
|---|---|---|
| `shopify-product-sync` | daily | Ingest (+ inline reconcile) |
| `skulabs-item-sync` | 10 min | Ingest (+ inline reconcile) |
| `full-reconcile` | nightly | Reconcile, full catalogue |
| `shopify-dispatch` | ~2 min | Dispatch |
| `skulabs-dispatch` | ~5 min | Dispatch |

Cadences are configuration (`ScheduledJobs` options), tunable without code changes.

## Manual & full sync

- **Per-item:** `POST /item-sync/{id}/sync` — reconcile(variant scope) → dispatch(variant
  scope), synchronous, returns the outcome. Honors the kill switches, bypasses the auto flags.
  Surfaced as a per-row **Sync** button in the Item Sync grid.
- **"Sync now" (full):** Hangfire job running import → item sync → full reconcile → both
  dispatchers. Same components as the scheduled paths; no special-case logic.

## Coordination infrastructure

There is **no message broker**. RabbitMQ (and the domain events `ProductVariantCreated/Updated`,
`SkulabsProductImported` with their consumers) was removed: every producer and consumer lived in
AppServer, so it was an external broker doing in-process messaging, while the only real
cross-host interaction (the "Sync now" button) already went through Hangfire on shared Postgres.

| Need | Covered by |
|---|---|
| Durable inbound queue | SQS (Shopify webhooks) |
| Cross-host work handoff (Web.Api → AppServer) | Hangfire on shared Postgres |
| Real-time propagation | Inline reconcile after ingest + immediate scoped dispatch |
| Retry / catch-up | Dirty rows + scheduled dispatcher runs |

## Invariants

1. Delete every trigger except `full-reconcile` and the dispatcher jobs → the system still
   converges. Nothing depends on real-time paths for correctness.
2. No component outside the dispatchers performs an external write.
3. A dirty bit is set exactly where a divergence is originated — by Ingest when it generates a
   SKU, by Reconcile for everything else. Cleared only by dispatchers on confirmed success.
4. Manual sync works with the auto flags off; nothing writes with the kill switch off.
5. The grid's pending list and the dispatchers' queues are the same query.

## Decision log

| Decision | Choice | Why |
|---|---|---|
| SKU generation location | Ingest, synchronous, same transaction | A variant row must never be visible without a SKU; downstream consumers cannot risk reading empty values |
| SKU push latency | Immediate scoped dispatch post-commit (seconds) | Generated SKUs must reach Shopify immediately, not on cadence |
| Title propagation | Dispatcher cadence (~5 min) | Cosmetic; no real-time requirement |
| Push instruction | Durable dirty state, not events | Retry, batching, rate-limit pacing, and UI visibility fall out of a durable queue; fire-and-forget messages provided none of them |
| Pending bit semantics | Queue *and* status — one state, one meaning | The earlier info-vs-instruction split was a symptom of one state trying to be two things |
| SkuLabs failure tracking | Counter + exclusion at 3, mirroring Shopify; rate limits exempt | Same operational story on both sides |
| RabbitMQ | Removed | Zero cross-host traffic in practice; Hangfire + dirty state cover every need at this scale |
| House term | **Dispatcher** | One name for the components that write to external systems |
