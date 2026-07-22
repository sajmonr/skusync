# Web API Conventions

`Web.Api` is an HTTP-only host. Endpoints use FastEndpoints and are organized by feature and use
case:

```text
Features/
└── ProductVariants/
    └── GetProductVariants/
        ├── Endpoint.cs
        ├── Request.cs
        ├── RequestValidator.cs
        ├── Response.cs
        └── ProductVariantGridMapper.cs
```

Keep endpoint request, validation, response, and mapping code together. Shared transport concerns
belong under `Common/`; business logic does not.

## Routes and responses

- FastEndpoints applies the `/api` prefix globally. Endpoint routes omit that prefix.
- Until authentication is implemented, endpoints explicitly call `AllowAnonymous()`.
- Return the documented response DTO directly. Do not wrap successful responses in a universal
  envelope.
- Paged endpoints return `PagedResponse<T>` with `items`, `totalCount`, `page`, and `pageSize`.
- Expected validation failures return HTTP 400 as `application/problem+json` using
  `ValidationProblemDetails` and a camel-case `errors` dictionary.
- Unhandled exceptions and empty HTTP error responses are converted to Problem Details by the
  ASP.NET Core exception and status-code middleware.
- Problem Details responses include `traceId` for correlation with server logs.

## Filtering, ordering, and paging

- Paged request DTOs inherit `GridQuery`.
- Their validators inherit `GridQueryValidator<TRequest>` and call `AddGridifyValidation(mapper)`.
- Every endpoint supplies an explicit `GridifyMapper<TEntity>`. Never call `GenerateMappings()`;
  only intentionally public query fields may be mapped.
- Mapper names describe the HTTP contract, not the database. For example,
  `failedSyncAttempts` may map to `FailedShopifySyncAttempts`.
- Apply Gridify to the entity query before the manual `Select` projection by calling
  `ToPagedResponseAsync`.
- Apply a deterministic default order to the source query so paging remains stable when the client
  omits `orderBy`.
- Keep EF queries as `IQueryable` through filtering, ordering, paging, and projection. Do not load
  complete entities before projection.

Example shape:

```csharp
var mapper = new GridifyMapper<ShopifyProductVariantEntity>()
    .AddMap("sku", entity => entity.Sku)
    .AddMap("failedSyncAttempts", entity => entity.FailedShopifySyncAttempts)
    .AddMap("updatedOn", entity => entity.UpdatedOnUtc);

return await dbContext.ShopifyProductVariants
    .AsNoTracking()
    .OrderBy(entity => entity.ShopifyProductVariantId)
    .ToPagedResponseAsync(
        request,
        mapper,
        entity => new ProductVariantListItem(
            entity.ShopifyProductVariantId,
            entity.Sku,
            entity.FailedShopifySyncAttempts,
            entity.UpdatedOnUtc),
        cancellationToken);
```

The projection is an expression translated by EF Core; AutoMapper or another runtime mapper is not
required.
