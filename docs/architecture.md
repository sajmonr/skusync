# SkuSync Sync-Pipeline Architecture

SkuSync bridges Shopify and SkuLabs. Postgres holds a **mirror of each system** — what Shopify
last said, what SkuLabs last said — plus a third table holding the **desired state**: what each
variant *should* hold. **Ingest** copies observations into the mirrors, **Reconcile** decides the
desired state, **Dispatch** pushes the difference. Everything else — the dashboard, manual sync,
feature flags, retry — hangs off those three stages.

```
            INGEST                       RECONCILE                      DISPATCH
 Shopify ──webhooks(SQS)──┐      ┌────────────────────────┐      ┌─ ShopifyDispatcher ──► Shopify
 Shopify ──import(job)────┼► mirrors │  IMergeRule chain   │ dirty│    (batch per product,
 SkuLabs ──item sync(job)─┘      │  one rule per field     │ bits─┤     retry, deactivate at 3)
                                 │  ▼                      │      └─ SkulabsDispatcher ──► SkuLabs
                                 │  desired state          │           (one bulk_upsert,
                                 └────────────────────────┘            paced, rate-limit aware)

  Postgres = mirrors + desired state + dirty queue + Hangfire.  SQS = inbound Shopify webhooks.
  Drain loop (~10s) is the fast path; recurring jobs are the failsafe.  No message broker.
```

## Principles

1. **Observing and deciding are separate.** A mirror records what an external system said. The
   desired state records what we concluded. Nothing writes both.
2. **Postgres is the hub and the only internal coordination layer** (data, dirty-state queue,
   Hangfire job storage). SQS remains the durable inbound queue for Shopify webhooks.
3. **Every field has exactly one owning rule**, enforced at startup. See §"Field authority".
4. **Correctness never depends on real-time paths.** Delete the drain loop and the immediate
   dispatch, and the system still converges on the recurring jobs — just slower.
5. **Dirty state is both the work queue and the UI status.** The Item Sync grid's pending column
   and the dispatchers' input are the same query.
6. **Manual and automatic paths share the same machinery**, differing only at the trigger.

## Stage 1 — Ingest (record what was said)

| Component | Trigger | Responsibility |
|---|---|---|
| Shopify webhook handlers (create / update / delete) | SQS | Mirror Shopify's variant state: create, refresh, mark deleted, reactivate. |
| Shopify product import (`ProductsService.SyncProducts`) | daily job + full sync | Same, full catalogue. |
| SkuLabs item sync | 10-minute job + full sync | Mirror SkuLabs items and the Shopify listings they report; link/relink to variants. An item with several listings is ambiguous, which follows from its listing count rather than being recorded anywhere. |

Rules:

- **Ingest decides nothing.** It copies fields verbatim, blanks included, and never substitutes a
  value of its own. A generated SKU written into the Shopify mirror would make the row claim Shopify
  holds something it does not, and the reconciler reads exactly that row to work out what Shopify is
  owed.
- Each ingest operation ends by invoking Reconcile for the variants it touched — **including those
  it did not change**. A payload matching the mirror still names variants whose decisions may be
  stale or absent.
- The origin is passed along (`MergeOrigin`), because two rules depend on it (§"Field authority").

## Stage 2 — Reconcile (the only place anything is decided)

Pure local computation: no feature flags, no external calls. Runs inline after every ingest, and as
a nightly full sweep.

For each variant it builds a `MergeContext` — the Shopify observation, the SkuLabs observation (or
nothing, when there is no usable link), and the current desired state — runs the registered
`IMergeRule` chain over it, and writes the outcome. Then it recomputes the dirty bits by comparing
desired against each mirror.

Key types (`Application/Sync/Merge`):

| Type | Role |
|---|---|
| `ItemField` | The decidable fields: `Sku`, `Barcode`, `Title`, `Location`. |
| `ObservedValue` | One field as a system reported it, keeping "we did not hear" apart from "they said empty". |
| `MergeContext` | Both observations, the running result, the origin, the reserved-SKU set. |
| `MergeResult` | Seeded from current values, dirty-tracked per property — silence means "leave alone". |
| `IMergeRule` | One decision. Declares `OwnedFields`. |
| `MergeRuleChain` | Runs the rules in sequence; **throws at startup if two claim the same field**. |

## Field authority

**The criterion is physical materialization, not which system is more authoritative.**

| Field | Pushed to Shopify | Pushed to SkuLabs | Materialized | Rule |
|---|---|---|---|---|
| sku | yes | **never** | **yes — printed labels** | `SkuMergeRule` |
| barcode | yes | **never** | **yes — printed labels** | `BarcodeMergeRule` |
| title | never | yes | no | `TitleMergeRule` |
| location | never | yes | no | `LocationMergeRule` |

**SkuLabs codes win whenever SkuLabs has one.** They are printed onto labels stuck to stock that
gets picked and shipped; preferring our own code over one already on a label makes that stock
unscannable. Whatever SkuLabs holds is accepted verbatim, including a value an operator typed by
hand. We therefore **never push a code to SkuLabs** — a constraint kept structural, since the
`bulk_upsert` payload type has no sku or barcode member to fill in.

**Titles and locations go the other way** because they are system-reference only, never relied on by
a picker holding paper. Same system, opposite treatment.

**Generation is a bid, not an assertion.** SKU generation exists to get a structured code into
Shopify before SkuLabs' own sync — on a cadence we neither see nor control — copies whatever is
there and freezes it. Losing that race costs tidiness, not correctness: the system converges on the
materialized code either way.

**A first sighting on a webhook distrusts the payload's codes; the import honours them.** The usual
way a variant appears on `products/create` is a merchant duplicating a product without clearing its
codes, so those are replaced. The import cannot apply the same rule: a SKU regenerated later would
not match the one generated when the variant was created, because the product may since have been
renamed and the SKU derives from the name. This is why `MergeOrigin` exists, and why the raw product
and variant titles are persisted — the generator abbreviates each separately, and a composed
"Product (Variant)" string cannot be split back apart.

## Stage 3 — Dispatch (the only external writers)

| | `ShopifyDispatcher` | `SkulabsDispatcher` |
|---|---|---|
| Drains | `PendingShopifySync` variants (active, not deleted, with a desired state) | `PendingSkulabsSync` items (linked, active) |
| Pushes | desired sku + barcode | desired title (+ location, once editable) |
| Batch shape | Group by product → one GraphQL mutation per product | Single `bulk_upsert` for the batch |
| On success | **Advance the mirror to the pushed values**, clear the bit, reset the counter | Same, provisionally — see below |
| On failure | Keep bit, increment counter, deactivate variant at 3 (+ audit event) | Keep bit, increment counter, exclude item at 3 (+ audit event) |
| Rate limit | n/a | `RateLimitedException` stops the run; rows stay dirty, **counters untouched** |
| Credential failure (401/403) | n/a | Rows stay dirty, **counters untouched** — see below |

**Advancing the mirror on success is what makes the dirty bit self-clearing.** Leaving it stale
would keep desired ≠ mirror and re-push the same values every cycle until an observation caught up.

For SkuLabs this write is **provisional**: `bulk_upsert` acknowledges with `{"success": true}` and
echoes no state, so the mirror takes what we sent and the next item sync replaces it with a real
observation. This is the one place the observations-only rule is knowingly bent, and it is bent
narrowly — we only ever push title and location, so sku and barcode in the SkuLabs mirror stay
exclusively ingest-written.

**Credential failures do not count against items.** An expired token or revoked scope fails every
batch identically; counting strikes would walk the whole catalogue to the exclusion threshold within
a few cycles over something one credential fix resolves. A 400 stays attributable to the batch and
keeps its strikes.

## Pacing and the SkuLabs rate limit

**Measured 2026-08-08: 104 requests/hour, per account, one pool across endpoints, derived from the
billing plan** (`basic_2025`, `api-2500-per-day`). Per-account matters more than the number: the
pool is shared with SkuLabs' own UI and any other consumer, and roughly 16/hour were already being
spent before we asked for anything.

Cost model — `item/get` does not paginate, and `bulk_upsert` sends the whole pending set in one
request. **Volume is therefore free and temporal spread is what costs**: 500 changes arriving
together cost 1 request, one change every 10s costs 360/hour.

The `SyncDrainLoop` (`BackgroundService`, ~10s tick) drains both targets. Shopify goes on every
tick; **SkuLabs is gated by a minimum push interval** (default 45s ⇒ ≤80 requests/hour).

A proactive budget was considered and rejected: the quota is spent by consumers we cannot
instrument, and no rate-limit headers come back on success, so a local counter could never correct
its own drift. An interval caps our contribution outright instead — and costs nothing in the common
case, because after any quiet spell it has already elapsed.

**429s carry no `Retry-After` header**; the wait is in the body at `error.data.wait_seconds`
(measured ~1508s). The cooldown is recorded under a single key, so it short-circuits ingest and
outbound alike: if the account is out of budget, it is out for everyone.

## State model

| Field | Meaning | Set by | Cleared by |
|---|---|---|---|
| `DesiredItemState.{Sku,Barcode,Title,Location}` | What both systems should hold | Reconcile (merge rules), deduplication | — |
| `ShopifyProductVariant.{Sku,Barcode,DisplayName}` | What Shopify last reported | Ingest; `ShopifyDispatcher` on confirmed success | — |
| `SkulabsItem.{Title,Sku,Barcode,Location}` | What SkuLabs last reported | Ingest; `SkulabsDispatcher` provisionally on success | — |
| `ShopifyProductVariant.PendingShopifySync` | desired ≠ Shopify mirror | Reconcile (recomputed) | `ShopifyDispatcher` on confirmed success |
| `SkulabsItem.PendingSkulabsSync` | desired ≠ SkuLabs mirror | Reconcile (recomputed) | `SkulabsDispatcher` on confirmed success |
| `FailedShopifySyncAttempts` / `IsActive` | Consecutive push failures / exclusion | `ShopifyDispatcher` | `ShopifyDispatcher` |
| `FailedSkulabsSyncAttempts` | Consecutive push failures / exclusion at 3 | `SkulabsDispatcher` | `SkulabsDispatcher` |

The pending bits are a **maintained cache of a computable fact** (`desired ≠ mirror`), kept as
columns so the grid can filter and sort against partial indexes. Reconcile recomputes rather than
accumulates them, so a mirror catching up clears the flag without anyone having to remember to.

## Feature flags

| Flag | Gates | Checked in |
|---|---|---|
| `ShopifySyncEnabled` | Shopify ingest (webhooks) | webhook handlers |
| `SkulabsSyncEnabled` | SkuLabs ingest (item sync) | item sync job |
| `ShopifyAutoDispatch` | scheduled + drain-loop + immediate Shopify dispatch | dispatch triggers |
| `SkulabsAutoDispatch` | scheduled + drain-loop SkuLabs dispatch | dispatch trigger |
| `ShopifyWriteBack` | **kill switch** — every Shopify write, incl. manual | `ShopifyDispatcher` push method |
| `SkulabsWriteBack` | **kill switch** — every SkuLabs write, incl. manual | `SkulabsDispatcher` push method |

Manual-only mode = kill switch ON + auto OFF: reconcile keeps deciding, the grid shows the queue,
and only the manual button pushes.

## Scheduled jobs and the drain loop

| Job | Cadence | Stage |
|---|---|---|
| `SyncDrainLoop` | ~10s tick (SkuLabs gated at 45s) | Dispatch — the fast path |
| `shopify-product-sync` | daily | Ingest (+ inline reconcile) |
| `skulabs-item-sync` | 10 min | Ingest (+ inline reconcile) |
| `full-reconcile` | nightly | Reconcile, full catalogue |
| `shopify-dispatch` / `skulabs-dispatch` | 10 min | Dispatch — failsafe behind the loop |

Cadences are configuration (`ScheduledJobs`, `SyncDrainLoop`), tunable without code changes.

## Manual & full sync

- **Per-item:** `POST /item-sync/{id}/sync` enqueues a `SingleItemSyncJob` and returns its Hangfire
  job id; poll `jobs/{id}`. Enqueued rather than run in the request because the SkuLabs quota is
  per-account: a push from the HTTP host spends the same allowance while escaping the drain loop's
  pacing and staying invisible to it.
- **"Sync now" (full):** Hangfire job running import → item sync → full reconcile → both dispatchers.

## Coordination infrastructure

There is **no message broker**. RabbitMQ was removed because every producer and consumer lived in
AppServer, so it was an external broker doing in-process messaging, while the only real cross-host
interaction already went through Hangfire on shared Postgres. That reasoning still holds: the
durable dirty state *is* the queue, and the drain loop is a faster listener on it, not a message bus.

| Need | Covered by |
|---|---|
| Durable inbound queue | SQS (Shopify webhooks) |
| Cross-host work handoff (Web.Api → AppServer) | Hangfire on shared Postgres |
| Real-time propagation | Inline reconcile after ingest + immediate scoped dispatch + drain loop |
| Retry / catch-up | Dirty rows + drain loop + scheduled dispatcher runs |

**SkuLabs webhooks:** SkuLabs *documents* `webhook/add-handler` with `item.updated` and
`item.inventory` event patterns, but a handler registered against a public endpoint received nothing
across multiple item edits when tested on 2026-08-08 (tunnel-level journal showed zero inbound
connections). The 10-minute poll remains the only reliable inbound signal. Notes for retrying are in
[`sync-pipeline-redesign.md`](sync-pipeline-redesign.md) §5.2.

## Invariants

1. Delete the drain loop and every immediate trigger → the system still converges on the recurring
   jobs. Nothing depends on real-time paths for correctness.
2. No component outside the dispatchers performs an external write.
3. Ingest writes only mirrors. Reconcile writes only desired state and the dirty bits. Dispatchers
   write externally, then advance the mirror they just pushed to.
4. Exactly one merge rule owns each field, enforced at startup.
5. A sku or barcode is never sent to SkuLabs — structurally, not by policy.
6. Manual sync works with the auto flags off; nothing writes with the kill switch off.
7. The grid's pending list and the dispatchers' queues are the same query.

## Decision log

| Decision | Choice | Why |
|---|---|---|
| Mirrors vs desired state | Separate tables | One row serving as both forced ingest to skip fields, refuse incoming values, and refresh metadata only when a link moved — each a workaround for a correction sharing a row with an observation |
| Desired state keyed by | Variant, not link | A generated SKU exists before any SkuLabs item does, and a link turning ambiguous must not take an un-pushed correction with it |
| Field authority criterion | Physical materialization | A value printed onto a label outranks one that only exists in a database — this, not "which system owns it", explains why codes and titles go opposite ways |
| Overlapping rule ownership | Startup failure | Authority that depends on which rule runs last makes registration order decide outcomes, invisibly |
| SKU generation location | Last link of the merge chain | It is a fallback decision like any other; as an ingest special case it had to write into a mirror |
| Codes pushed to SkuLabs | Never — payload type has no such member | A rule can be misconfigured; an absent field cannot be filled in |
| Webhook-create vs import codes | Force-generate vs honour | Duplicated products arrive via create with stale codes; a re-derived SKU cannot be matched against the original |
| Pending bits | Kept as a recomputed cache | Grid filters and sorts need indexed columns; recomputing rather than accumulating removes the "forgot to set the bit" class of bug |
| Dispatch pacing | Minimum interval, not a budget | A per-account quota spent by uninstrumented consumers, with no headers on success, gives a local counter nothing to correct against |
| Drain loop hosting | `BackgroundService`, not Hangfire | Hangfire's cron floor is one minute; a 10s job would write thousands of rows a day recording that there was nothing to do |
| SkuLabs mirror after push | Provisional write-what-was-sent | `bulk_upsert` acknowledges without echoing state; leaving the mirror stale would re-push every cycle |
| Manual per-item sync | Enqueued, not in-request | Keeps every SkuLabs request inside the host whose pacing governs the shared quota |
| Message broker | Still none | Durable dirty state is the queue; the drain loop is a faster listener, not a bus |
| House term | **Dispatcher** | One name for the components that write to external systems |
