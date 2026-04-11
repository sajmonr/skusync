# SkuSync — Agent & Developer Guide

Monorepo with a .NET 10 backend and a Node.js/React Shopify app frontend.

---

## Project Structure

```
skusync/          # Node.js Shopify app (React Router 7, TypeScript, Prisma, SQLite)
dotnet/           # .NET 10 backend (Clean Architecture, EF Core, PostgreSQL)
  src/
    Web.Api/          — ASP.NET Core controllers, OpenAPI
    Application/      — Business logic, use cases, Quartz jobs, services
    Integration/      — Shopify & AWS SQS integrations
    Infrastructure/   — EF Core DbContext, health checks
    SharedKernel/     — Common types, abstractions, extensions
  test/
    Tests.Application/    — Unit tests for business logic
    Tests.Integration/    — Integration tests
    ArchitectureTests/    — Clean Architecture rule enforcement
```

---

## Build & Run

### Backend (.NET)

```bash
cd dotnet
dotnet restore SkuSync.slnx
dotnet build SkuSync.slnx
dotnet test SkuSync.slnx

# Run locally (PostgreSQL on port 5433)
docker compose up
```

### Frontend (Node.js)

```bash
cd skusync
npm install
npm run setup       # Prisma generate + migrate
npm run dev         # Requires Shopify CLI
npm run build
npm run typecheck
npm run lint
```

---

## Service Construction

Services use **primary constructors** with interface dependencies. No `readonly` field declarations are needed — dependencies are accessed directly.

```csharp
public class ProductsService(
    IShopifyProductService shopifyProductService,
    ApplicationDbContext dbContext,
    ILogger<ProductsService> logger,
    IEventAccumulator<ProductChangedEvent> eventAccumulator) : IProductsService
{
    public async Task<ProductImportResult> ImportProductsFromShopify()
    {
        // use shopifyProductService, dbContext, logger directly
    }
}
```

Services are registered in `DependencyInjection` extension classes per layer, using the C# `extension<T>` pattern on `IHostApplicationBuilder`.

---

## Exception Handling

**Service layer:** Catch exceptions, log them, and return a typed result object. Do not re-throw or let exceptions propagate from services.

```csharp
try
{
    shopifyVariants = await shopifyProductService.GetProducts();
}
catch (Exception exception)
{
    logger.LogError(exception, "An exception occurred while fetching products from Shopify.");
    return ProductImportResult.Failure(
        "Could not import products from Shopify because the products could not be fetched.");
}
```

**Quartz jobs:** Wrap unexpected exceptions in `JobExecutionException` with `refireImmediately: false` so the scheduler is properly notified.

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "ShopifySyncJob failed after {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
    throw new JobExecutionException(ex, refireImmediately: false);
}
```

**Configuration exceptions:** Use custom exception types (e.g. `OptionsConfigurationSectionNotFoundException`) for startup failures where termination is appropriate.

---

## Logging

Use structured logging with named placeholders (never string interpolation). Choose the level based on intent:

| Level | When to use |
|-------|-------------|
| `Debug` | Operational detail — counts, per-item progress, field-level changes, timing |
| `Information` | Workflow milestones — job started/completed, batch summaries |
| `Warning` | Unexpected but recoverable conditions |
| `Error` | Exceptions, business logic failures, operations that could not complete |

```csharp
logger.LogDebug("Fetched {Count} product variants from Shopify.", shopifyVariants.Length);
logger.LogInformation("Deduplication complete. Modified {Count} variant(s).", affectedVariantIds.Length);
logger.LogError(exception, "An exception occurred while saving product variants to the database.");
```

Always include elapsed milliseconds for timed operations:

```csharp
logger.LogDebug("Job finished in {ElapsedMs}ms. Next fire time: {NextFireTime}.",
    stopwatch.ElapsedMilliseconds, context.NextFireTimeUtc?.ToString("o") ?? "none");
```

---

## Result Objects

**Prefer result objects over `bool` returns or thrown exceptions for expected failure paths.**

Results are `readonly record struct` types with `Success`/`Failure` factory methods:

```csharp
public readonly record struct ProductImportResult(bool IsSuccess, int Created, int Updated, string Error)
{
    public static ProductImportResult Success(int created, int updated) => new(true, created, updated, "");
    public static ProductImportResult Failure(string error) => new(false, 0, 0, error);
}
```

Callers check `IsSuccess` before consuming the payload:

```csharp
var importResult = await productsService.ImportProductsFromShopify();
if (!importResult.IsSuccess)
{
    logger.LogError("Import failed: {ErrorMessage}", importResult.Error);
    return;
}
```

---

## Domain Models

- Use **`readonly record struct`** for value objects, external API response models, and result types (immutable, stack-friendly, structural equality).
- Use **`record`** (class) for events and lightweight data carriers that are passed by reference.
- Use **mutable classes** only for EF Core entities that require tracked property setters.
- Avoid plain classes for new domain types unless mutation is genuinely required.

```csharp
// Value object / API model
public readonly record struct ShopifyProductVariant(
    string GlobalProductId, string GlobalVariantId,
    string ProductTitle, string VariantTitle, string Sku, string Barcode);

// Event
public record ProductChangedEvent(long VariantId, ProductChangeType ChangeType);

// Result
public readonly record struct ProductDeduplicationResult(bool IsSuccess, long[] VariantIds, string Error)
{
    public static ProductDeduplicationResult Success(long[] variantIds) => new(true, variantIds, "");
    public static ProductDeduplicationResult Failure(string error) => new(false, [], error);
}
```

---

## Method Length & Decomposition

**Methods should remain focused and short.** If a method grows beyond ~40 lines, extract private helpers. Keep the public method as an orchestrator — reading it should convey intent without implementation detail.

```csharp
// Public orchestrator: ~30 lines, reads like a workflow
public async Task<ProductImportResult> ImportProductsFromShopify()
{
    var shopifyVariants = await FetchShopifyVariantsAsync();
    if (shopifyVariants is null) return ProductImportResult.Failure("...");

    var (created, updated, events) = await SyncVariantsAsync(shopifyVariants);

    var saved = await TrySaveChangesAsync();
    if (!saved) return ProductImportResult.Failure("...");

    eventAccumulator.Enqueue(events);
    return ProductImportResult.Success(created, updated);
}

// Private helpers handle the complexity
private static bool UpdateVariant(ShopifyProductVariantEntity existing, ShopifyProductVariant incoming)
{
    // ...
}
```

---

## Scheduled Jobs (Quartz.NET)

Jobs live in `Application/` and implement `IJob`. Always apply:
- `[DisallowConcurrentExecution]` — prevents overlapping executions.
- `[MutexGroup("group-name")]` — for concurrency control across related jobs.

```csharp
[DisallowConcurrentExecution]
[MutexGroup("shopify-sync")]
public class ShopifySyncJob(
    IProductsService productsService,
    ILogger<ShopifySyncJob> logger) : IJob
{
    public static readonly JobKey Key = new(nameof(ShopifySyncJob), "shopify");

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("ShopifySyncJob started.");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await productsService.ImportProductsFromShopify();
            stopwatch.Stop();

            if (!result.IsSuccess)
            {
                logger.LogError("ShopifySyncJob failed: {ErrorMessage}", result.Error);
                return;
            }

            logger.LogInformation("ShopifySyncJob completed in {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "ShopifySyncJob threw after {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
```

Register jobs via `AddScheduledJob<TJob>` extension:

```csharp
builder.Services.AddQuartz(quartz =>
{
    quartz.AddScheduledJob<ShopifySyncJob>(ShopifySyncJob.Key, scheduledJobsOptions.ShopifyProductSync);
});
```

`JobScheduleOptions` controls `Enabled`, `CronExpression`, and `RunOnStart` per-job via `appsettings.json`.

---

## Unit Tests

**Frameworks:** xUnit · NSubstitute (mocking) · Shouldly (assertions)

**Pattern:** AAA (Arrange / Act / Assert) with a `CreateSut()` factory method.

```csharp
public class ShopifySyncJobTests
{
    private readonly IProductsService _productsService = Substitute.For<IProductsService>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<ShopifySyncJob> _logger = new();

    [Fact]
    public async Task Execute_ShouldCallImportProducts()
    {
        var sut = CreateSut();

        await sut.Execute(_context);

        await _productsService.Received(1).ImportProductsFromShopify();
    }

    [Fact]
    public async Task Execute_ShouldThrowJobExecutionException_WhenImportProductsThrows()
    {
        _productsService.ImportProductsFromShopify().ThrowsAsync(new InvalidOperationException("fail"));
        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        thrown.RefireImmediately.ShouldBeFalse();
    }

    private ShopifySyncJob CreateSut() => new(_productsService, _logger);
}
```

- Use **in-memory EF Core** (`UseInMemoryDatabase(Guid.NewGuid().ToString())`) for service tests that need a real `DbContext`.
- Implement a **`TestLogger<T>`** to capture and assert log entries when logging behavior matters.
- Inject `TestLogger<T>` via the same `ILogger<T>` interface — no mocking frameworks needed for it.

```csharp
private sealed class TestLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
}

private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
```

---

## Event Accumulation Pattern

Services enqueue domain events **after** `SaveChangesAsync()` succeeds to avoid publishing phantom events on DB failure.

```csharp
// Collect first, enqueue only after a successful save
var pendingEvents = new List<ProductChangedEvent>();
// ... populate pendingEvents during entity loop ...

await dbContext.SaveChangesAsync();          // throws on failure — caught above

eventAccumulator.Enqueue(pendingEvents);    // only reached on success
```

`IEventAccumulator<T>` is registered as a singleton. `ProductEventProcessorJob` drains it on its own schedule.

---

## Database Entities & Configuration

Entities live in `src/Infrastructure/Database/Entities/`. Each entity has a corresponding `IEntityTypeConfiguration<T>` in `src/Infrastructure/Database/Configuration/`, which is auto-discovered via `modelBuilder.ApplyConfigurationsFromAssembly(...)`.

### Naming conventions

| Rule | Example |
|---|---|
| Entity class names end with `Entity` | `ShopifyProductVariantEntity` |
| Table names are **plural** | `"ShopifyProductVariants"` |
| Primary key property matches `{ClassName}Id` | `ShopifyProductVariantId` |
| Timestamp properties are suffixed with `Utc` | `CreatedOnUtc`, `UpdatedOnUtc` |

### Primary keys

All primary keys are **UUIDv7**, set client-side via `Guid.CreateVersion7()` on the entity initializer and backed by a `uuidv7()` SQL default. Use the `HasUuidV7PrimaryKey` extension in the configuration class:

```csharp
builder.HasUuidV7PrimaryKey(x => x.ShopifyProductVariantId);
```

This calls `ValueGeneratedNever()` on the .NET side so EF Core does not attempt to auto-generate the value, while the SQL default covers database-side inserts.

### Required vs optional columns

**All columns are required by default.** Mark a property optional in the configuration only when there is an explicit domain reason for it to be nullable. Always call `.IsRequired()` explicitly in the configuration for clarity, even where EF Core would infer it.

### Timestamps

All datetime columns store **UTC** values. Use the `HasDefaultValueDateTimeNowUtcSql()` extension for columns that default to the current time on insert:

```csharp
builder.Property(x => x.CreatedOnUtc)
    .IsRequired()
    .HasDefaultValueDateTimeNowUtcSql();   // → now() at time zone 'utc'
```

The C# property should also default to `DateTime.UtcNow` so the value is correct when the entity is used in-memory before being persisted:

```csharp
public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
```

Never store local time. Never use `DateTime.Now`.

### Entity class structure

```csharp
// Infrastructure/Database/Entities/ExampleEntity.cs
namespace Infrastructure.Database.Entities;

public class ExampleEntity
{
    public Guid ExampleId { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = "";

    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedOnUtc { get; set; } = DateTime.UtcNow;
}
```

### Configuration class structure

```csharp
// Infrastructure/Database/Configuration/ExampleConfiguration.cs
using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ExampleConfiguration : IEntityTypeConfiguration<ExampleEntity>
{
    public void Configure(EntityTypeBuilder<ExampleEntity> builder)
    {
        builder.ToTable("Examples");                         // plural table name

        builder.HasUuidV7PrimaryKey(x => x.ExampleId);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.CreatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();

        builder.Property(x => x.UpdatedOnUtc)
            .IsRequired()
            .HasDefaultValueDateTimeNowUtcSql();
    }
}
```

### DbContext

Add an explicit `DbSet<T>` property to `ApplicationDbContext` for every entity that services need to query or write to directly:

```csharp
public DbSet<ExampleEntity> Examples { get; init; }
```

The configuration is discovered automatically — no manual `modelBuilder.Entity<T>()` call needed.

---

## CI/CD

- GitHub Actions: `.github/workflows/build.yml` (builds and tests the .NET solution only).
- NuGet versions are centrally managed in `dotnet/Directory.Packages.props`.
