# SkuSync

[![Staging deploy (develop)](https://github.com/sajmonr/skusync/actions/workflows/staging-deploy.yml/badge.svg?branch=develop)](https://github.com/sajmonr/skusync/actions/workflows/staging-deploy.yml)
[![Production deploy (main)](https://github.com/sajmonr/skusync/actions/workflows/prod-deploy.yml/badge.svg?branch=main)](https://github.com/sajmonr/skusync/actions/workflows/prod-deploy.yml)

Monorepo with a .NET 10 backend and a Node.js / React Shopify app frontend.
The backend keeps Shopify product variants in sync with SkuLabs inventory items;
the frontend is the embedded Shopify admin app.

## Project structure

```
skusync/   # Node.js Shopify app (React Router 7, TypeScript, Prisma)
dotnet/    # .NET 10 backend  (Clean Architecture, EF Core, PostgreSQL)
```

## Backend (.NET 10)

Solution file: `dotnet/SkuSync.slnx`.

### Build & test

```bash
cd dotnet
dotnet restore SkuSync.slnx
dotnet build   SkuSync.slnx
dotnet test    SkuSync.slnx
```

### Run locally

```bash
cd dotnet
docker compose up --build
```

This brings up the full stack: PostgreSQL (exposed on host port `5433`), Seq for
structured logs (UI at <http://localhost:8081>), the `web.api` HTTP host
(<http://localhost:8080>), and the `app.server` background worker. Postgres
credentials live in [`dotnet/.env`](dotnet/.env); each host's Shopify / SkuLabs /
AWS configuration is read from its .NET user secrets, bind-mounted into the
container, so no secret environment variables need to be set.

### Architecture

Clean Architecture, with these projects under `dotnet/src`:

| Project          | Responsibility                                     |
|------------------|----------------------------------------------------|
| `Web.Api`        | ASP.NET Core HTTP host — OpenAPI + health endpoints. Serves traffic only. |
| `AppServer`      | Generic Host worker — owns all background processing: SQS webhook consumption, Shopify webhook handlers, in-memory event consumers, and Quartz jobs. |
| `Application`    | Business logic, use cases, Quartz jobs, events     |
| `Integration`    | Shopify and SkuLabs API clients, AWS SQS poller    |
| `Infrastructure` | EF Core `DbContext`, migrations, health checks     |
| `SharedKernel`   | Common types and abstractions                      |

The backend runs as **two hosts** sharing the same layers: `Web.Api` serves HTTP,
and `AppServer` runs the background workloads. Both run the coordinated startup
migration — guarded by a Postgres advisory lock so only one migrates at a time —
before starting.

Test projects under `dotnet/test`:

| Project              | Purpose                                                         |
|----------------------|-----------------------------------------------------------------|
| `Tests.Application`  | Unit tests for services, jobs, webhook handlers, consumers       |
| `Tests.Integration`  | HTTP client tests (SkuLabs, Shopify), DI wiring                 |
| `Tests.E2E`          | End-to-end scenarios booting the `AppServer` host against Postgres (Testcontainers) + WireMock |
| `Tests.Architecture` | Clean Architecture rule enforcement (NetArchTest)               |

## Frontend (Node.js)

Located in `skusync/`. Shopify embedded app built with React Router 7 and Vite.

```bash
cd skusync
npm install
npm run setup       # Prisma generate + migrate
npm run dev         # Dev server (requires Shopify CLI)
npm run build
npm run typecheck
npm run lint
```

Key files:

- `app/shopify.server.ts` — Shopify app initialization
- `app/db.server.ts` — Prisma client
- `app/routes/` — File-based routing

## CI / CD

GitHub Actions workflows in `.github/workflows/`:

- `staging-deploy.yml` — builds and deploys the .NET backend to staging on
  pushes to `develop`.
- `prod-deploy.yml` — builds and deploys to production on pushes to `main`.

The status badges at the top of this file report on the staging workflow run
against each branch.

## Notes

- NuGet package versions are centrally managed in
  [`dotnet/Directory.Packages.props`](dotnet/Directory.Packages.props).
- Frontend uses SQLite (dev) via Prisma; the backend uses PostgreSQL via EF Core.
- The SkuLabs HTTP client wraps a retry pipeline (`Microsoft.Extensions.Http.Resilience`)
  that honours `Retry-After` while capping any single wait at 60 seconds to keep
  the scheduled job slot responsive.
- Feature flags live in `appsettings*.json` under `FeatureManagement` and are
  consumed via `IFeatureManager`. See [`Application/FeatureFlags.cs`](dotnet/src/Application/FeatureFlags.cs)
  for the registered flag names.
