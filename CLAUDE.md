# SkuSync

Monorepo with a .NET 10 backend and a Node.js/React Shopify app frontend.

## Project Structure

```
skusync/          # Node.js Shopify app (React Router 7, TypeScript, Prisma)
dotnet/           # .NET 10 backend (Clean Architecture, EF Core, PostgreSQL)
```

## Backend (.NET)

Solution file: `dotnet/SkuSync.slnx`

### Build & Test

```bash
cd dotnet
dotnet restore SkuSync.slnx
dotnet build SkuSync.slnx
dotnet test SkuSync.slnx
```

### Run locally

```bash
cd dotnet
docker compose up
```

PostgreSQL runs on port 5433 locally.

### Architecture

Clean Architecture with these layers:
- `src/Web.Api` — ASP.NET Core controllers, OpenAPI
- `src/Application` — Business logic, use cases, Quartz jobs
- `src/Integration` — Shopify & AWS SQS integrations
- `src/Infrastructure` — EF Core DbContext, health checks
- `src/SharedKernel` — Common types and abstractions

Test projects:
- `test/Tests.Application` — Unit tests for business logic
- `test/Tests.Integration` — Integration tests
- `test/ArchitectureTests` — Clean Architecture rule enforcement

## Frontend (Node.js)

Located in `skusync/`. Shopify app built with React Router 7 and Vite.

### Build & Test

```bash
cd skusync
npm install
npm run setup       # Prisma generate + migrate
npm run dev         # Dev server (requires Shopify CLI)
npm run build
npm run typecheck
npm run lint
```

### Key files

- `app/shopify.server.ts` — Shopify app initialization
- `app/db.server.ts` — Prisma client
- `app/routes/` — File-based routing

## Notes

- CI/CD: `.github/workflows/build.yml` (GitHub Actions, .NET only)
- Frontend uses SQLite (dev) via Prisma; backend uses PostgreSQL via EF Core
- NuGet versions are centrally managed in `dotnet/Directory.Packages.props`

## Backend conventions

### Naming
- **No `Async` suffix on method names.** Codebase convention is suffix-free (`ImportProductsFromShopify`, `GetProducts`, `Sync`, `Handle`, `Generate`). Framework methods (`SaveChangesAsync`, `AnyAsync`, `IsEnabledAsync`, …) keep their original names because they belong to Microsoft APIs.
- Method names describe behaviour, not implementation. `ReconcileVariants`, not `LoopOverShopifyVariants`.

### Method size
- Aim for methods under ~100 lines. Not a hard limit — the goal is a single scannable unit of intent. When a method grows past that, look for natural seams (a loop body, an option-resolution branch, an event-publishing block) and extract a private helper with a descriptive name.

### Types
- Prefer `readonly record struct` for small data carriers — value equality, no allocation. `ShopifyProductVariant`, `ProductVariantCreatedEvent` follow this.
- `record class` only when reference identity or inheritance is needed.
- Plain `class` for EF entities (they need reference identity for change tracking), services, options.

### Access modifiers
- Default to the narrowest visibility that compiles. Helpers used by one class are `private`. `internal` is correct for layer-internal collaborators not part of the public API. `public` is for the layer's contract.
- `sealed` on test helper classes and types not intended as base classes.
- Don't widen visibility just to make a test easier — test through the public API or use a seam.

### Logging — two parallel channels, both matter

**Console logging via `ILogger<T>`** — for operators and Seq dashboards:
- `LogDebug`: routine progress (loop counts, "loaded N variants").
- `LogInformation`: domain milestones (SKU generated, variant created, sync completed).
- `LogError(exception, ...)`: every `catch`. Always pass the exception as the first argument so the stack trace is preserved.
- Use structured placeholders (`{VariantId}`, `{Sku}`), never `$"..."` interpolation — Serilog/Seq lose the structure otherwise.

**Database log events via `ShopifyProductVariantLogEvent`** — per-variant audit trail merchants can read:
- Use `VariantLogMessages.*` factory methods so wording stays consistent.
- One row per meaningful change: `VariantCreated`, `SkuSet`, `SkuUpdated`, `BarcodeSet`, `TitleUpdated`, etc.
- For brand-new entities: `entity.LogEvents.Add(...)` so events flow with the insert.
- For existing entities: `dbContext.ShopifyProductVariantLogEvents.Add(...)` directly.

### Error handling
- Throw `InvalidOperationException` (or a more specific type) for programmer/config errors with an actionable message including the offending input.
- Catch at the outermost orchestration boundary — typically the public method of a service — and convert to a domain result type (`ProductImportResult.Failure(...)`) rather than letting exceptions reach a Quartz job.
- In webhook handlers, let exceptions propagate to the SQS message handler so the message is retried by SQS.
- Wrap framework boundaries (DB, HTTP, external APIs) in try/catch with a descriptive log; never swallow.

### Configuration
- One options class per feature, with `[Required]` / `[Range]` data annotations. Bind via `builder.AddOptionsFromConfiguration<T>(T.SectionKey)` (extension on `IHostApplicationBuilder`).
- Defaults in `appsettings.json`; environment overrides in `appsettings.{Environment}.json`. Secrets stay blank in committed files — supplied via env vars or user secrets locally.
- Feature flags via `Microsoft.FeatureManagement` (`IFeatureManager.IsEnabledAsync(FeatureFlags.X)`).

### Dependency injection
- Each layer has a `DependencyInjection.cs` exposing an `IHostApplicationBuilder` extension method (`AddApplication`, `AddInfrastructure`, `AddIntegration`). New services register inside the matching layer's file.
- Prefer `AddTransient` for stateless services. `AddSingleton` for genuinely singleton resources (HTTP clients via factory, message bus). Avoid `AddScoped` outside of EF/DbContext.

### EF Core
- Entity primary keys are UUIDv7 (`Guid.CreateVersion7()`), set in the entity initializer.
- Indexes and column constraints live in `Infrastructure/Database/Configuration/<Entity>Configuration.cs`.
- Schema changes require a migration: `dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Web.Api`. Don't hand-edit the snapshot.

### NuGet
- All package versions centralized in `dotnet/Directory.Packages.props`. `.csproj` files reference the package by name only; no inline version pins.

## Testing

### Unit tests (`Tests.Application`, `Tests.Integration`)
- **Frameworks**: xUnit + Shouldly + NSubstitute. Use `ShouldBe`, `ShouldContain`, `ShouldThrowAsync` — never `Assert.*`. Mock with `Substitute.For<T>()`.
- **DbContext**: in-memory provider per test class, `UseInMemoryDatabase(Guid.NewGuid().ToString())` so tests don't share state.
- **Naming**: `Method_ExpectedBehaviour[_WhenCondition]`. Examples: `Handle_ShouldPersistOneEntity_PerVariant`, `Generate_AppendsDashOne_WhenBaseSkuCollidesInDatabase`.
- **Structure**: Arrange-Act-Assert with blank-line separators; one logical assertion concept per test.
- **NSubstitute + async returns**: set a default return value in the constructor for any `Task<T>` method the SUT might call — `_skuGenerator.Generate(...).Returns(_ => Task.FromResult("..."))`. Otherwise the mock returns `null` and the SUT crashes deep inside production code.
- **Loggers in tests**: don't `Substitute.For<ILogger<T>>()` — NSubstitute can't intercept the generic methods reliably. Use a small `TestLogger<T>` (see existing tests) that buffers entries for assertions, or `NullLogger<T>.Instance` when you don't need to assert on log output.
- **Seeding helpers**: each test class has its own `SeedX(...)` private method so arrange blocks stay short.

### E2E tests (`Tests.E2E`)
- Real PostgreSQL via `Testcontainers.PostgreSql`; WireMock for outbound HTTP we own; `Substitute.For<IShopifyGraphQlService>()` for Shopify (avoids HTTPS/cert handshake against ShopifySharp).
- All E2E classes share the factory via `[Collection(E2ETestCollection.Name)]` — tests within the collection run **serially** because env-var-based config is process-wide.
- Each test calls `factory.ResetAsync()` in `InitializeAsync` to wipe variant + log-event tables and clear recorded mock calls between runs.
- Dispatch webhooks via `factory.DispatchWebhookAsync(envelope)` so the real `SqsShopEventProductHandler` topic routing is exercised.
- Use `AsyncWait.UntilAsync(...)` for async post-conditions (consumers running on the in-memory bus).

## Comments and docs
- Default to **no inline comments**. Only add `///` XML docs on public types/members so IDE tooltips work.
- Inline comments explain non-obvious **why** (a hidden constraint, a workaround for a specific incident, an invariant that would surprise a reader). Never restate **what** the code does.
- Don't reference current tasks, PRs, or callers in comments — those belong in the PR description and rot quickly.

## Git
- Feature work on a branch off `develop`, in a worktree under `.claude/worktrees/<name>`. PRs target `develop`. Don't push to `main` directly.
- Commit subjects: imperative, short (≤72 chars). Body explains the why and any non-obvious impact.
- Don't `--no-verify`. Don't force-push `main` or `develop`.
- Never commit `appsettings.*` files with real secrets.
- Don't commit IDE artefacts (`.idea/**` workspace state, `.vs/`, etc.) — leave them out of stages.
- Pull requests follow `.github/pull_request_template.md`. Fill in the Summary and Test plan; drop the Notes section if you have nothing to add.
