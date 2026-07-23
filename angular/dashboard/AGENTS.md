# Angular Dashboard Development Standards

These instructions apply to all work under `angular/dashboard/`.

## Architecture

Organize application code by business feature. Do not create application-wide folders that group
unrelated code only by technical type, such as a root `components/`, `services/`, or `models/`
folder.

```text
src/app/
├── core/       # Application-wide infrastructure and transport contracts
├── features/   # Business capabilities and routed pages
├── layout/     # Application shell, navigation, sidebar, and topbar
├── shared/     # Domain-neutral reusable UI and utilities
├── app.config.ts
├── app.routes.ts
└── app.ts
```

Dependencies flow toward shared infrastructure:

```text
features ──> shared
    │          │
    └────────> core

layout ─────> shared/core
core ───────> no features
shared ─────> no features
```

- A feature must not import another feature's internal files.
- `core` and `shared` must never import from `features`.
- Keep code in its owning feature first. Move it to `shared` only after at least two unrelated
  features need it and it contains no domain-specific behavior or vocabulary.
- Avoid broad `index.ts` barrel files. Prefer direct imports so dependencies remain visible.

## Feature Structure

Create only the folders a feature actually needs:

```text
features/product-variants/
├── pages/                 # Routed orchestration components
├── components/            # Feature-specific presentation components
├── data-access/           # HTTP clients and feature state
├── models/                # API DTOs and feature view models
├── utilities/             # Pure feature-specific functions
└── product-variants.routes.ts
```

- Page components coordinate route state, data loading, and user actions.
- Feature components may understand domain concepts but should not make HTTP calls.
- API DTOs stay with the consuming feature unless they are genuinely cross-feature transport
  contracts.
- Keep editable form models separate from API DTOs when their shapes diverge.
- Lazy-load top-level features from `app.routes.ts`.

## Components

- Do not introduce component-level explicit foreground, background, border, shadow, or status
  colors unless the user specifically asks for a color treatment. Rely on the established global
  theme and component library so light and dark modes remain consistent.
- Use PrimeNG components and directives for dashboard UI controls by default. Use native controls
  only when PrimeNG has no suitable component or the native element is materially simpler and does
  not need visual styling.
- Use standalone components. Angular v22 uses `OnPush` by default, so do not add an explicit
  `changeDetection` setting unless intentionally opting into `ChangeDetectionStrategy.Eager` or
  `ChangeDetectionStrategy.Default` for an exceptional case.
- The application is zoneless by default. Do not add ZoneJS or `provideZoneChangeDetection()`.
  Ensure template updates are scheduled through signals, changed inputs, template listeners,
  `AsyncPipe`, or an explicit `markForCheck()` notification.
- Prefer signal inputs, signal outputs, computed signals, and local signals for new code.
- Use Signal Forms from `@angular/forms/signals` for new forms and filter controls. Prefer a
  signal-backed form model, `[formField]`, and schema rules such as `debounce()` over Reactive
  Forms unless integration constraints require the older API.
- Keep a component's TypeScript, template, stylesheet, and tests together.
- Use page components for orchestration and keep reusable UI components presentational.
- Keep HTTP calls and feature state out of shared UI components.
- Do not wrap every PrimeNG component. Add a wrapper only when it enforces a recurring application
  convention or supplies meaningful behavior.
- Use PrimeNG v22 APIs and documentation from <https://primeng.dev/>.
- Preserve accessible labels, keyboard operation, focus visibility, and semantic HTML.

## Services and State

- Put application-wide infrastructure in `core`, including API configuration, interceptors,
  authentication, logging, notifications, and global error handling.
- Put feature-specific services in `features/<feature>/data-access`.
- Name services by responsibility. Examples: `ProductVariantsStore`,
  `ProductVariantMutationsService`, and `NotificationService`.
- Prefer `httpResource` for reactive GET requests. Drive its request from signals and consume its
  value, loading, and error signals directly; it preserves configured `HttpClient` interceptors and
  cancels obsolete requests when reactive dependencies change.
- Use `HttpClient` directly for mutations and imperative request workflows. API services perform
  those requests and transport mapping. Stores coordinate feature state, loading status, query
  options, and actions.
- Use root provision only for intentionally application-wide singleton services. Provide feature
  state at the route or page level when its lifetime should match that feature.
- Do not introduce a generic HTTP wrapper over `HttpClient`; feature API services should use
  `HttpClient` directly while shared interceptors handle cross-cutting transport concerns.
- Use a plain function instead of an injectable service for stateless transformations that have no
  dependencies.

## Pipes, Directives, and Utilities

- Pipes are small, pure display transformations. Do not use pipes for server-backed filtering,
  ordering, paging, API work, or complex business decisions.
- Keep domain-specific pipes and directives in their feature. Put only domain-neutral ones in
  `shared/pipes` or `shared/directives`.
- Utilities must be pure functions. Avoid miscellaneous `helpers` or `utils` dumping grounds;
  name files for the operation they provide.
- Product table filtering, ordering, and paging remain server-driven through the standardized API
  query contract rather than transforming complete collections in the browser.

## HTTP Contracts

- Keep shared transport types such as `ProblemDetails` and `PagedResponse<T>` in `core/api`.
- Keep endpoint-specific request and response DTOs inside the owning feature.
- Successful endpoints return their documented DTO directly; do not expect a universal success
  envelope.
- Errors use Problem Details. Validation errors extend Problem Details with an `errors` dictionary.
- HTTP interceptors handle transport-wide concerns only. Feature-specific error interpretation
  belongs in feature data-access or orchestration code.

## Testing and Naming

- Co-locate unit tests as `*.spec.ts` beside the code under test.
- Test observable behavior rather than private implementation details.
- Use kebab-case filenames that match the responsibility of the exported symbol.
- Use explicit suffixes when they communicate architectural responsibility, such as `-page`,
  `-api.service`, `-store`, or `.routes`.
- Run `npm test -- --watch=false` and `npm run build` before completing Angular changes.
