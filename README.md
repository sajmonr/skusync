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
docker compose up
```

PostgreSQL is exposed on port `5433` on the host.

### Architecture

Clean Architecture, with these projects under `dotnet/src`:

| Project          | Responsibility                                     |
|------------------|----------------------------------------------------|
| `Web.Api`        | ASP.NET Core controllers, OpenAPI, app composition |
| `Application`    | Business logic, use cases, Quartz jobs, events     |
| `Integration`    | Shopify and SkuLabs API clients, AWS SQS poller    |
| `Infrastructure` | EF Core `DbContext`, migrations, health checks     |
| `SharedKernel`   | Common types and abstractions                      |

Test projects under `dotnet/test`:

| Project              | Purpose                                                         |
|----------------------|-----------------------------------------------------------------|
| `Tests.Application`  | Unit tests for services, jobs, webhook handlers, consumers       |
| `Tests.Integration`  | HTTP client tests (SkuLabs, Shopify), DI wiring                 |
| `Tests.E2E`          | End-to-end scenarios against Postgres (Testcontainers) + WireMock |
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
