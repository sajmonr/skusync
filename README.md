# SkuSync

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

Bring up your own PostgreSQL (the app expects it on `localhost:5433` by default — see
[`process-compose.yaml`](process-compose.yaml)), then launch the apps with
[process-compose](https://github.com/F1bonacc1/process-compose):

```bash
process-compose --no-server up
```

This starts the `web-api` HTTP host (<http://localhost:5257>) and the Angular
dashboard (<http://localhost:4200>). The `--no-server` flag prevents Process Compose
from binding its HTTP server to port `8080`. The database connection string is set
in [`process-compose.yaml`](process-compose.yaml); each host's Shopify / SkuLabs / AWS
configuration is read from its .NET user secrets, which `dotnet run` loads
automatically in Development, so no secret environment variables need to be set.
Background processing (`app.server`) is not started here.

The dashboard proxies `/api` requests to the local Web.Api host. Use
`GET http://localhost:5257/api/status` to verify the REST connection directly.

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

**CI** — GitHub Actions verifies pull requests (`.github/workflows/`):

- `pr-develop.yml` — build + unit/integration/architecture tests (E2E excluded)
  on PRs targeting `develop`.
- `pr-main.yml` — full build + tests, **including** E2E, on PRs targeting `main`.

**Build & deploy** — handled by [Dokploy](https://dokploy.com) on a dedicated
build server, which builds each backend service from its Dockerfile
([`dotnet/src/Web.Api/Dockerfile`](dotnet/src/Web.Api/Dockerfile) and
[`dotnet/src/AppServer/Dockerfile`](dotnet/src/AppServer/Dockerfile)). Image
builds and deployments no longer run in GitHub Actions.

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
