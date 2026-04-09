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
