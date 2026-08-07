# SkuSync Sync-Pipeline Redesign — Design Notes

> Companion document for the "Sync pipeline redesign" GitHub issue. Captures the full design
> discussion (2026-08-07) so work can start cold from this file. Nothing here is implemented
> yet; `docs/architecture.md` describes the current system.

## 1. Context and motivation

Today's pipeline (see `docs/architecture.md`: Ingest → Reconcile → Dispatch) works, but the
variant row plays two roles at once: it is the **Shopify mirror** *and* the **authoritative
desired state**. Every awkward spot in the code traces back to that dual role:

- `SkulabsItemSyncService` refreshes item metadata only when the link moves ("metadata follows
  the link") — because refreshing the mirror would clobber an undispatched local correction.
- `ShopifyProductUpdateWebhookHandler.DidBarcodeOrSkuChange` *refuses* to write incoming
  Shopify sku/barcode — because writing them would destroy the authoritative value. This is a
  field-authority rule hiding in a webhook handler.
- Field-authority rules live in three unconnected places (Reconciler, update webhook handler,
  item sync service) with no shared vocabulary.

A new requirement is coming: **functionality that needs dispatch to Shopify and SkuLabs within
~30–60 seconds** (current dispatch crons: Shopify every 2 min, SkuLabs every 5 min; generated
SKUs already push within seconds via the inline immediate dispatch).

## 2. Target data model — observed vs desired state (three tables)

The Kubernetes spec/status split applied to two-way sync:

| Table | Holds | Written by |
|---|---|---|
| Shopify variant mirror | What Shopify currently has | Ingest (webhooks, import) + dispatch success (from push *response*) |
| SkuLabs item mirror | What SkuLabs currently has | Ingest (item sync) + dispatch success (see below) |
| **Desired state** (new) | The reconciled truth | Reconcile (merge rules) only |

Key decisions:

- **The desired-state row hangs off the variant, not the link.** Two reasons:
  (a) SKU generation applies to *unlinked* variants — a new variant pushes a generated SKU to
  Shopify with no SkuLabs item in sight; (b) links churn (ambiguity, listing moves) — desired
  state must survive a broken link, possibly holding un-pushed corrections. A broken/ambiguous
  link just removes the SkuLabs observation from the merge inputs (`SkulabsItemLinks.IsSyncable`
  already defines when that input counts).
- **Ingest becomes pure copy.** No decisions, no dirty bits, no authority logic. The
  `DidBarcodeOrSkuChange` special case and the metadata-follows-the-link rule are both deleted.
  All decision-making concentrates in Reconcile; Dispatch is decision-free draining.
- **Dirty is computed, not asserted:** dirty-toward-Shopify = `desired ≠ shopifyMirror` on
  Shopify-pushable fields; same for SkuLabs. Stored bits may remain as an indexed cache, but
  they demote to a cache of a computable fact. Per-field granularity falls out.
- **Mirrors are written from observations only.** A push *response* counts as an observation;
  what we *sent* does not (targets can normalize/reject). Per side:
  - Shopify: `productVariantsBulkUpdate` is GraphQL — select the resulting variant state in the
    mutation response and write the mirror from it. The echo `products/update` webhook arrives
    seconds later and re-ingests as a confirming no-op (pure-copy ingest makes the echo safe).
  - SkuLabs: the `bulk_upsert` response body is currently discarded (`SkulabsItemClient` checks
    status only). If the response echoes item state, use it; if it is ack-only, write-what-you-
    sent as a provisional observation and let the 10-min item sync confirm.
  - **The mirror write on success is not optional:** without it, a pushed row still computes as
    dirty until the next observation (up to 10 min for SkuLabs) and the drain loop re-pushes it
    every cycle — burning the SkuLabs rate budget (~40 redundant calls per item).
- **UI win:** while a row is dirty, the grid can show a per-field pending-change preview
  ("SkuLabs currently has X → will become Y") straight from a mirror-vs-desired diff.
- **`DisplayName` composition** (product title + variant title) is currently done at ingest;
  with pure-copy ingest, store both raw and make composition a derivation in the merge.
- The desired table doubles as a **three-way merge base**: after a successful push both mirrors
  equal desired, so a later mirror-vs-desired drift identifies *which side changed*. Current
  rules are static authority and don't need this, but it gives future rules room ("the side
  that changed wins") with no schema change.
- Desired-row lifecycle: variant deleted → desired row frozen with it; link ambiguous →
  SkuLabs input drops out of the merge, Shopify-side sync continues.
- Keep the current invariant that the Item Sync grid's pending column and the dispatchers'
  input are the same query (now a join).

## 3. Merge rules (`IMergeRule`) — one merge point

- Signature shape: `(observedShopify, observedSkulabs, currentDesired) → newDesired`, operating
  on a **source-agnostic snapshot type** (e.g. `ItemSnapshot { Title, Sku, Barcode }`), not on
  EF entities or Shopify/SkuLabs API models. Mapping happens at the edges.
- `Result` is **seeded from `Current`** and dirty-tracked per property — silence means "no
  change", never "blank this field". The per-property dirty flags drive: which columns to
  write, pending computation, and the old/new pair for `VariantLogMessages` audit rows.
- **"Blank is not a claim" lives in the context, not in each rule** (e.g. `New.Sku.HasValue`).
  History shows this regresses when left to rule authors (`aec9a0b`, `7e20a89`).
- Rules are chained, but **no two rules in one chain may govern the same field** — validate at
  composition time and throw at startup. Field authority is a property of a field, not a
  pipeline position; DI registration order must never silently decide outcomes.
- **Discrimination by provenance via marker type parameters** (`IMergeRule<ShopifyIngest>`,
  `IMergeRule<SkulabsIngest>`, …) — DI resolves `IEnumerable<IMergeRule<T>>` natively.
  Provenance has **two axes**: source system *and* lifecycle event (see §4 — webhook-create
  uses a different SKU rule than import).
- Naming: `IMergeRule` / `MergeContext`, implementations like `ShopifyTitleMergeRule` —
  rules *decide authority*, they don't perform updates.
- **SKU generation moves from "ingest exception" into the merge** as the last link of a
  priority chain. The invariant "a variant row is never visible without a SKU" transfers to the
  desired table (the mirror is allowed to truthfully show Shopify's blank); reconcile runs
  inline after ingest so the desired row is born with its SKU in the same unit of work.
- Scope boundary: the merge covers field values only. Variant creation, deletion detection,
  reactivation are lifecycle, not merge — keep them out or `IMergeRule` becomes a god
  abstraction.

## 4. Authority model — the business rules (record these; they read as bugs otherwise)

### 4.1 SkuLabs sku/barcode are physically materialized

SkuLabs is the inventory system: items get **tagged, printed, and shipped** using SkuLabs
sku/barcode values. Once a code is set in SkuLabs it may be on physical labels, so:

- SkuLabs sku/barcode are **observe-only**. We NEVER push sku/barcode to SkuLabs — under any
  circumstance, including blank-in-Shopify. The `bulk_upsert` payload carrying only
  `{_id, name}` is **load-bearing, not a gap** — keep the constraint structural (the payload
  type has no sku/barcode members).
- We accept whatever SkuLabs holds, verbatim, including manual operator edits (assume the
  operator knows what they're doing).
- SkuLabs behavior (confirmed): when SkuLabs syncs a new Shopify listing it copies non-blank
  sku/barcode from Shopify; if blank it **generates random values**. It **never updates its own
  sku/barcode afterward** (only a human operator can).

Field × direction matrix (every field is strictly one-directional):

| Field | We push to Shopify | We push to SkuLabs | SkuLabs is |
|---|---|---|---|
| sku / barcode | yes | **never** | observe-only source |
| title | never | yes | write target |

*(Open question: are titles printed on labels too? If titles must freeze once tagged, the title
write-back needs the same treatment and SkuLabs becomes entirely read-only.)*

### 4.2 SKU generation is a *bid* in a naming race

Our generation exists to get structured codes (`BW-…`) into Shopify **before SkuLabs's own
sync** (unknown, irregular cadence) copies blank/stale values and freezes them. Losing the race
is not corruption — the system converges to whatever SkuLabs materialized (random codes on
labels are ugly, not wrong). **Correctness never depends on winning**; speed only narrows the
window. The window is dominated by Shopify webhook delivery, which we don't control.

Downtime scenario (verified against current code — converges correctly): system down → product
created blank → SkuLabs random-generates `XYZ` → recovery processes the create webhook →
we generate `BW-…`, push to Shopify → next item sync links the item →
`RecurringJobs.SyncSkulabsItems` inline-reconciles → SkuLabs wins → `XYZ` pushed to Shopify.
`BW-…` exposure in Shopify: ≤ ~12 min, never reaches SkuLabs.

### 4.3 Webhook-create vs import generate differently — ON PURPOSE

- **Webhook create** (`ShopifyProductCreateWebhookHandler`, newly-seen branch of the update
  handler): **force-generate regardless of the payload sku**. Merchants duplicate products
  without clearing sku/barcode, so a creation payload's codes are presumed duplicates.
- **Import** (`ProductsService.ResolveSkuForNewVariant`): honor non-blank Shopify values,
  generate only for blanks. A re-derived SKU can't be matched against what was originally
  generated (the product may have been renamed; the SKU derives from the name), so non-blank
  values must be accepted.

As merge rules the sku chain differs by lifecycle provenance:

- webhook-create: `linked SkuLabs observation → generate` (deliberately ignore Shopify payload)
- import/newly-seen: `linked SkuLabs observation → non-blank Shopify → generate`

Consequences discussed:

- The dedup-by-forcing policy is also a race. Losing branch: SkuLabs syncs the duplicated
  product first, copies duplicate `ABC`, freezes it → our forced `BW-…` gets overwritten back
  to `ABC` by reconcile → **two SkuLabs items permanently share a code**. Accepted trade
  (window is seconds in normal operation), but:
  - **Surface duplicate-SKU collisions** (reconciler mirroring an item SKU that already exists
    on another variant) as a dashboard/grid warning or distinct log event — never block
    (SkuLabs is verbatim-authoritative), never silent.
- **Local unresolved-listing adoption:** on create, before generating, check the local listing
  table for an unresolved listing whose `RawVariantId` matches the incoming variant id (the
  SkuLabs item may already be in our DB from a downtime window). If found, adopt the item's
  codes instead of generating — a purely local check that eliminates doomed-push churn. Falls
  out automatically once generation is a merge rule over all observations.
- **Audit the discarded merchant SKU:** force-on-create currently logs `SkuSet(BW-…)` but not
  that the merchant's `ABC` was discarded or why. Add a log message ("replaced supplied SKU
  `ABC` per duplicate-prevention policy") for the "where did my SKU go?" support question.

## 5. Dispatch and latency design

Decision: **no message broker** (RabbitMQ stays removed — the `015e134` rationale still holds:
producers and consumers all live in AppServer; the one cross-host interaction goes through
Hangfire on shared Postgres). The dirty state **is** the event queue; the fix is a faster
listener, not a megaphone.

Target flow:

1. Webhook arrives → ingest pure-copies into the mirror (if different)
2. **Inline reconcile** for the touched scope (pure local computation, milliseconds — do NOT
   make reconcile an async event) → merge rules write desired; dirty falls out
3. **Drain loop** — a `BackgroundService` with `PeriodicTimer` in AppServer, ~15s cadence,
   drains dirty rows batched per target. Meets the upcoming ~30–60s requirement with margin.
4. **Inline immediate scoped dispatch stays** for SKU origination (seconds path — the naming
   race of §4.2 depends on it).
5. Scheduled dispatch crons relax to ~10 min pure failsafe (or fold into the loop entirely);
   nightly full-reconcile stays.

Why a `BackgroundService` and not more Hangfire: Hangfire recurring cron has a 1-minute floor.
Below it, the in-Hangfire workaround (a self-rescheduling job chain) is fragile (a broken chain
needs a watchdog; schedule-poller latency stacks) and a 15s recurring job would write ~5,800
job/state rows a day of mostly "nothing dirty, exited". The loop's durability/retry/status
needs are all served by the dirty-state design itself; the loop is a queue consumer, not a
scheduled job. If tolerance were ≥ ~60s, a plain 1-minute Hangfire recurring job would be
correct and no loop should exist.

Rate limits: the drain loop coalesces naturally — a burst of 50 changes in 10s becomes one
`bulk_upsert`, not 50 calls. Existing `RateLimitedException` handling (rows stay pending,
counters untouched) composes unchanged: a rate-limited cycle just means the next cycle drains
a bigger batch.

Scheduler decision: **keep Hangfire, do not reintroduce Quartz.** What survives the redesign —
daily import, 10-min item sync (the only SkuLabs inbound signal; no webhooks), nightly
reconcile, "Sync now" cross-host enqueue (`TriggerProductSyncEndpoint`), job-status polling
(`GetJobStatusEndpoint` reads the Hangfire monitoring API), duplicate-trigger dedupe, automatic
retries — leans on Hangfire-specific features Quartz lacks out of the box (no monitoring API,
no built-in retry, no maintained dashboard). Swapping would rebuild those by hand. Quartz's one
real edge (seconds-granularity cron) only buys out of ~20 lines of idiomatic hosting code.

Independent Web.Api fix: `TriggerItemSyncEndpoint` currently calls both dispatchers
**synchronously inside the HTTP request** — Web.Api makes outbound Shopify/SkuLabs calls and
independently spends the SkuLabs rate budget alongside AppServer. Move to enqueue-and-poll
(same shape as `TriggerProductSyncEndpoint`) so AppServer is the sole owner of outbound
traffic.

Also noted: `Dispatch` has no concurrency guard (plain read-then-push) — overlapping
`DispatchAll` runs race on the same pending rows. Today it is benign (idempotent double-push);
with more triggers, scope enqueued dispatches to explicit IDs and/or serialize the drain.

## 6. Suggested sequencing

Quick wins, independent of the redesign (can land first, any order):

1. `TriggerItemSyncEndpoint` → enqueue-and-poll (removes dispatch from Web.Api).
2. Duplicate-SKU collision surfacing (log event / grid warning).
3. Unresolved-listing adoption before generation in the create path.
4. Audit event for the discarded merchant SKU on force-generate.
5. Docs: record §4.1/§4.3 rationale in `docs/architecture.md`'s decision log + why-comments at
   the generation sites; remove the stale "#91 target architecture" banner (it landed:
   `b6a44fc`, `f22fc57`, `015e134`).

Redesign PR chain:

1. Add the desired-state table + backfill (today's variant row IS the desired state — the seed
   is well-defined). Reconciler writes desired; dispatchers read desired; mirror-on-success
   writes.
2. Strip ingest to pure copy: delete `DidBarcodeOrSkuChange` and metadata-follows-the-link;
   move `DisplayName` composition into the merge.
3. Land `IMergeRule` on the single merge point (incl. generation-as-rule with provenance-scoped
   chains per §4.3).
4. Drain loop + cadence relaxation; keep inline immediate dispatch for SKU origination.
5. Rewrite `docs/architecture.md` (state model, invariant 3, and the ingest-exception
   paragraph all change).

Test strategy note: `c8f57cc` rewrote the tests to assert **pipeline convergence, not
mechanism** — exactly the harness that lets the mechanism be swapped underneath. E2E
(Testcontainers Postgres + WireMock SkuLabs mock, `Substitute.For<IShopifyGraphQlService>`)
should carry the refactor.

## 7. Open questions

1. Does the SkuLabs `bulk_upsert` response echo the resulting item state, or is it ack-only?
   (Decides the mirror-write source; fallback = provisional write-what-you-sent + 10-min
   confirm.)
2. Are titles printed on labels / must they freeze once tagged? (Decides whether title
   write-back to SkuLabs survives; currently assumed it survives.)
3. What does an omitted field do in `bulk_upsert` — leave the value alone or blank it?
   (Must confirm a title-only push can never blank an existing SkuLabs sku.)
4. Shopify `productVariantsBulkUpdate` response selection — confirm the mutation can return
   the resulting variant fields needed for the mirror write.
5. Exact latency requirement of the upcoming near-real-time feature (30–60s assumed).

## 8. Key files

| Area | Files |
|---|---|
| Recurring jobs | `dotnet/src/shared/Application/Jobs/RecurringJobRegistrar.cs`, `RecurringJobs.cs`, `ScheduledJobsOptions.cs`, `dotnet/src/AppServer/appsettings.*.json` (`ScheduledJobs`) |
| Reconcile | `dotnet/src/shared/Application/Sync/Reconciler.cs` |
| Dispatch | `Sync/ShopifyDispatcher.cs`, `Sync/SkulabsDispatcher.cs`, `Sync/ShopifyDispatchTrigger.cs` |
| Ingest — Shopify | `Products/Webhook/ShopifyProductCreateWebhookHandler.cs`, `ShopifyProductUpdateWebhookHandler.cs`, `Products/Services/ProductsService.cs` (`ResolveSkuForNewVariant`) |
| Ingest — SkuLabs | `Skulabs/Services/SkulabsItemSyncService.cs` |
| SkuLabs client | `dotnet/src/shared/Integration/Skulabs/Items/SkulabsItemClient.cs` (`UpdateItems`, `BulkUpsertItem`) |
| Web.Api | `Features/ItemSync/TriggerItemSync/TriggerItemSyncEndpoint.cs`, `Features/ProductSync/TriggerProductSyncEndpoint.cs`, `Features/Jobs/GetJobStatusEndpoint.cs` |
| Docs | `docs/architecture.md` |
