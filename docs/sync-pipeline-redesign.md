# SkuSync Sync-Pipeline Redesign — Design Notes

> Companion document for the "Sync pipeline redesign" GitHub issue. Captures the full design
> discussion (2026-08-07, revised 2026-08-08) so work can start cold from this file. Nothing
> here is implemented yet; `docs/architecture.md` describes the current system.
>
> **2026-08-08 revision** — the original open questions are largely settled: §4.1 gains the
> title and location rulings, §4.4 covers location as the second pushable field, §5.1 records
> the SkuLabs rate budget as the dominant constraint (the *pacing mechanism* is explicitly still
> undecided), §5.2 records that SkuLabs webhooks do not fire, and §7 is now a resolution log
> rather than a question list.

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
  - SkuLabs: **ack-only, confirmed** — `bulk_upsert` returns `{"success": true}` and nothing
    else (§7 Q1). So the mirror write is necessarily *provisional write-what-you-sent*, with the
    10-min item sync as the confirming observation. This is the one place the "observations
    only" rule is knowingly bent, and it is bent narrowly: we only ever push `name` and
    (soon) `location`, so sku/barcode in the SkuLabs mirror stay exclusively ingest-written and
    §4.1's observe-only guarantee holds structurally.
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

**Titles and locations are NOT materialized** — they are system-reference values only, never
relied on by a picker holding paper. That is the whole reason they are safe to overwrite while
sku/barcode are not: same external system, opposite treatment, and the distinction is invisible
from the code. Materialization, not "which system owns it", is the criterion.

Field × direction matrix (every field is strictly one-directional):

| Field | We push to Shopify | We push to SkuLabs | SkuLabs is | Materialized |
|---|---|---|---|---|
| sku / barcode | yes | **never** | observe-only source | **yes — printed labels** |
| title | never | yes | write target | no |
| location | never | yes *(new — see §4.4)* | write target | no |

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

### 4.4 Location — the second pushable field (the driver for this work)

`SkulabsItemEntity.Location` landed in #102 (`05c7f0d`) as **ingest-only**: the item's bin in the
configured warehouse, mirrored from `alias_locations`. The upcoming functionality makes it
**editable from Shopify**, which turns it into the second field we push to SkuLabs and is the
reason the latency question in §5 exists at all.

Consequences:

- `SkulabsItemUpdateWithId(Id, Name)` and the `{_id, name}` wire body must both grow a location
  member. **This is the first time the `bulk_upsert` payload shape has ever changed**, which is
  exactly when an omitted-field-blanks-the-value bug would bite — see §7 Q2, still an unverified
  assumption. Suggested mitigation: land the blank-transition detector (§6 #6) first.
- Ingest already distinguishes three states (`null` = no warehouse configured so we never asked;
  `""` = asked, item has no bin; a value). Outbound needs the same care so "this item has no
  bin" and "we don't know" cannot collapse into the same wire value. **Decide explicitly what
  "clear the location" sends.**
- Nothing parses or validates a location — SkuLabs owns the format and we mirror it verbatim,
  including real-world noise (`A-01-6`, `c-12-03`, `AA-02-11`). Editing from Shopify should not
  introduce validation that the ingest side does not apply.

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

### 5.1 The SkuLabs rate budget is the dominant constraint

**No per-field cadence differentiation.** All fields are pushed and ingested as fast as the
budget allows; we do not build "title is cosmetic so it can wait" logic. What gets controlled is
the *cadence of the inbound and outbound loops*, not which field rides which loop.

Shopify is not a problem — webhooks inbound, a generous limit outbound. **SkuLabs is, because
ingest and outbound compete for one tight budget.** Working figure: **~45 requests/hour,
UNCONFIRMED** — confirm with SkuLabs, and ask three things: is it per-key or per-account, is it
uniform across endpoints, and can it be raised. If `item/get` and `item/bulk_upsert` have
separate budgets, ingest and outbound do not compete and most of this section is moot.

Cost model (verified against the code):

- `GetAllItems` does **not** paginate — one `GET item/get?fields=…` returns the whole array.
  One request per ingest pass, regardless of catalogue size.
- `bulk_upsert` sends the entire pending set in **one** request. 1 dirty item and 500 dirty
  items cost exactly the same.

**Therefore volume is free and temporal spread is what costs.** This inverts the intuition and
must not be forgotten:

| Change pattern | Requests/hour |
|---|---|
| 500 changes arriving together | 1 |
| 1 change every 10s, sustained | 360 — 8× over budget |

A bulk import is nearly free; a steady trickle is the worst case. Any throttling keyed on
*number of dirty rows* would optimise the wrong variable. (Corollary: the intuition that "if too
many items are dirty we must fall back to the periodic sync" is backwards — a large batch is the
cheap case.)

Order-of-magnitude consequence, if the ~45/hour figure holds: a 10-minute ingest cadence costs 6,
leaving ~39 for outbound — roughly **one push per 90s sustained**. The ~30–60s target would then
not be achievable as a *sustained* guarantee, though the common case (an isolated edit, budget
in hand) could still be fast. At ~200/hour the sustained interval drops to ~18s and the target
is met comfortably, which is why **confirming — and if possible raising — the limit is the
highest-leverage unknown on this workstream**.

What the existing code does today: `IRateLimitService` / `SkulabsRateLimitHandler` handle 429s
*reactively* (record a cooldown, defer), and `RateLimitedException` leaves rows pending with
counters untouched. There is no *proactive* budgeting — nothing decides in advance whether a
request is affordable, and nothing arbitrates between ingest and outbound.

> **UNDECIDED — how to pace the two loops against a shared budget.** Options discussed but not
> settled: a shared proactive budget/token-bucket both loops draw from; a reservation floor so
> ingest cannot be starved by outbound; decoupling tick frequency from spend frequency so
> detection stays fast while spending stays paced. Ingest starvation is the risk worth weighing —
> it carries the §4.1 materialized values, so stale ingest means pushing outdated sku/barcode at
> Shopify, whereas outbound lag is only a UX cost. **Settle this before the drain-loop work in
> §6 is scoped**; the cadence numbers in §5 above are provisional until then.

### 5.2 SkuLabs webhooks: documented, but not working

SkuLabs exposes `webhook/add-handler` and `webhook/remove-handler`, with event patterns that
include `item.updated` and `item.inventory`. This contradicts the claim in `docs/architecture.md`
that SkuLabs gives us no webhooks — but only on paper.

**Tested 2026-08-08 and it did not fire.** A handler was registered against a public ngrok
endpoint and multiple items were edited; ngrok's tunnel-level request journal recorded zero
inbound connections (i.e. SkuLabs never opened one — not a listener, routing, or response-format
problem). Notes for anyone retrying:

- The documented event names are prose ("common patterns: …"), not an enum — `item.update` vs
  `item.updated` is unverified.
- There is **no list-handlers endpoint**, only add and remove (by `_id`), so a registration
  cannot be confirmed after the fact except by whatever `add-handler` returned.
- Untested discriminator: register a *different* event (e.g. `item.inventory`) on a distinct
  path. If that fires and `item.updated` does not, it is a per-event problem; if neither fires,
  it is account- or scope-level (the API preamble notes routes vary between `platformApi`,
  `platformGeneric`, and `platformUser` scopes, and we authenticate with an API key).

**Working assumption: the 10-minute poll remains the only reliable inbound signal.** If webhooks
are ever made to work they slot into Ingest as one more trigger — no structural change to this
design — and would shrink the §4.2 naming-race window from ~12 minutes to seconds. Treat as a
separate spike, not a dependency. `docs/architecture.md` needs a nuanced correction here: not
"SkuLabs has no webhooks" but "SkuLabs documents webhooks; they did not fire when tested".

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
   `b6a44fc`, `f22fc57`, `015e134`); correct the "no SkuLabs webhooks" premise per §5.2.
6. **Blank-transition detector** — log when a linked item's sku/barcode goes non-blank → blank
   between ingest passes. Today such a wipe would be *silent*: the reconciler treats blank as
   "not a claim", so it propagates nowhere and logs nothing, and we would only learn from the
   warehouse. **Prerequisite for the location push (§4.4).**
7. **Parse the `bulk_upsert` 200 body**; treat `success != true` as a batch failure routed into
   the existing `RecordFailedAttempt` path.
8. **Use `user_error` for retry decisions** — `SkulabsErrorPayload.UserError` is already parsed
   but only logged. Per the spec it means "caused by user input", i.e. retrying identical input
   cannot help: `true` → exclude immediately with the audit event, `false` → retry as today.
   Currently a malformed item burns three identical retries first.
9. **Fix the SkuLabs mock**: `mocks/skulabs/mappings/item-bulk-upsert.json` returns
   `{"items": []}`, which is invented — the real response is `{"success": true}`. Add a 400
   `{error: {...}}` stub too; nothing currently exercises the error path against a realistic body.

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

## 7. Resolution log (was: open questions)

**Q1 — does `bulk_upsert` echo item state? RESOLVED: no, ack-only.** Returns `{"success": true}`
(required boolean). All documented failures are non-2xx carrying `{error: {…}}`, so
`EnsureSuccessStatusCode()` does cover the documented paths — but since `success` is a boolean
rather than a constant, and the docs are vague on when it goes false, parse it anyway (§6 #7).
`SkulabsErrorPayload` already matches the spec field-for-field; only the cosmetic `type` and
`display` hints are unmapped. Config verified: the endpoint pins `servers: api.skulabs.com`,
which is what Production and Staging use.

**Q2 — does omitting a field in `bulk_upsert` blank it? UNRESOLVED, and now load-bearing.** The
OpenAPI request schema declares the item object as `properties: {}` — it documents nothing. Our
only evidence is indirect: production has pushed title-only bodies for months and warehouse
scanning still works. Note *why* that evidence comes from the warehouse and not from us — a wipe
would be silent locally (§6 #6). The location push (§4.4) changes the payload shape for the
first time, so land the detector first.

**Q3 — are titles materialized? RESOLVED: no.** Titles are system-reference only. Title
write-back survives, `SkulabsDispatcher` and `PendingSkulabsSync` stay, matrix unchanged (§4.1).

**Q4 — can the Shopify mutation return variant state for the mirror write? RESOLVED: yes.**
`ShopifyProductService` currently selects only `userErrors { field message }`;
`productVariantsBulkUpdate` returns `product` / `productVariants` / `userErrors`, so extending
the selection to `productVariants { id sku barcode }` is a one-line change in code we own.

**Q5 — what is the near-real-time functionality? RESOLVED: editing `location` from Shopify**
(§4.4). Direction is **outbound**. No per-field cadence differentiation — everything moves as
fast as the budget allows. The real question turned out not to be latency but the shared SkuLabs
rate budget (§5.1).

### Still open

1. **Confirm the SkuLabs rate limit** — the ~45/hour working figure is unverified. Per-key or
   per-account? Uniform across endpoints? Can it be raised? Highest-leverage unknown here.
2. **Q2 above** — omitted-field semantics in `bulk_upsert`.
3. **What does "clear the location" send outbound** (§4.4), given ingest's `null` vs `""`
   distinction.
4. **Does `bulk_upsert` ever return 200 with `success: false`**, and does it identify the failing
   item? If a bad item can fail a whole batch, the batch-bisection knob listed as hypothetical in
   `docs/architecture.md` stops being hypothetical.
5. **SkuLabs webhooks** (§5.2) — parked as a separate spike, not a dependency.

## 8. Key files

| Area | Files |
|---|---|
| Recurring jobs | `dotnet/src/shared/Application/Jobs/RecurringJobRegistrar.cs`, `RecurringJobs.cs`, `ScheduledJobsOptions.cs`, `dotnet/src/AppServer/appsettings.*.json` (`ScheduledJobs`) |
| Reconcile | `dotnet/src/shared/Application/Sync/Reconciler.cs` |
| Dispatch | `Sync/ShopifyDispatcher.cs`, `Sync/SkulabsDispatcher.cs`, `Sync/ShopifyDispatchTrigger.cs` |
| Ingest — Shopify | `Products/Webhook/ShopifyProductCreateWebhookHandler.cs`, `ShopifyProductUpdateWebhookHandler.cs`, `Products/Services/ProductsService.cs` (`ResolveSkuForNewVariant`) |
| Ingest — SkuLabs | `Skulabs/Services/SkulabsItemSyncService.cs` |
| SkuLabs client | `dotnet/src/shared/Integration/Skulabs/Items/SkulabsItemClient.cs` (`GetAllItems`, `UpdateItems`, `BulkUpsertItem`), `SkulabsItemUpdateWithId.cs`, `SkulabsErrorResponse.cs` |
| Rate limiting | `Integration/RateLimiting/*` (`IRateLimitService`, `RateLimitedException`), `Skulabs/Items/SkulabsRateLimitHandler.cs`, `SkulabsResiliencePipeline.cs` — reactive 429 handling only; see §5.1 |
| Location | `Infrastructure/Database/Entities/SkulabsItemEntity.cs` (`Location`), `Integration/Skulabs/Items/SkulabsApiItem.cs`, `SkulabsItemResponse.cs` (`alias_locations`), `Skulabs/Options/SkulabsApiOptions.cs` (`WarehouseId`) |
| SkuLabs mock | `mocks/skulabs/mappings/*.json`, `mocks/skulabs/__files/skulabs-items.json`, `mocks/skulabs/README.md` |
| Web.Api | `Features/ItemSync/TriggerItemSync/TriggerItemSyncEndpoint.cs`, `Features/ProductSync/TriggerProductSyncEndpoint.cs`, `Features/Jobs/GetJobStatusEndpoint.cs` |
| Docs | `docs/architecture.md` |
