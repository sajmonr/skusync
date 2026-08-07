using System.Security.Claims;
using System.Text;
using Infrastructure.Database;
using Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Web.Api;

namespace Tests.E2E.Infrastructure;

public class WebApiTestHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The shop this host is configured to serve. Tokens for any other shop are rejected.</summary>
    public const string ShopUrl = "https://e2e-web-api.myshopify.com";

    public const string ShopifyClientId = "e2e-shopify-client-id";

    // HS256 signing needs at least a 256-bit key, so this is deliberately long.
    private const string ShopifyClientSecret = "e2e-shopify-client-secret-at-least-32-bytes";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly Dictionary<string, string?> originalEnvironmentValues = [];

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();

        SetEnvironmentVariable("ConnectionStrings__SkuSync", postgres.GetConnectionString());
        SetEnvironmentVariable("DashboardAuthentication__Password", "test-password");
        SetEnvironmentVariable("DashboardAuthentication__BypassOnDevelopment", "false");
        SetEnvironmentVariable("Shopify__ShopUrl", ShopUrl);
        SetEnvironmentVariable("Shopify__App__ClientId", ShopifyClientId);
        SetEnvironmentVariable("Shopify__App__ClientSecret", ShopifyClientSecret);
    }

    /// <summary>
    /// Mints a session token shaped like the ones Shopify hands to admin UI extensions. Defaults
    /// produce a token this host accepts; override an argument to exercise a rejection path.
    /// </summary>
    public string CreateSessionToken(
        string? shop = null,
        string? clientId = null,
        string? signingSecret = null,
        TimeSpan? lifetime = null)
    {
        var issuedAt = DateTime.UtcNow;
        var destination = shop ?? ShopUrl;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = $"{destination}/admin",
            Audience = clientId ?? ShopifyClientId,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt.Add(lifetime ?? TimeSpan.FromMinutes(1)),
            Claims = new Dictionary<string, object>
            {
                ["dest"] = destination,
                ["sub"] = "42",
                ["sid"] = Guid.NewGuid().ToString(),
                ["jti"] = Guid.NewGuid().ToString()
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret ?? ShopifyClientSecret)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Seeds one Shopify variant, optionally linked to a SkuLabs item, and returns its numeric
    /// Shopify variant ID. Pass <paramref name="productId"/> to put several variants on one product;
    /// left unset, each variant gets a product of its own.
    /// </summary>
    public async Task<long> SeedVariant(
        long variantId,
        string? skulabsSourceItemId,
        bool isDeleted = false,
        bool isActive = true,
        long? productId = null)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var owningProductId = productId ?? variantId * 100;

        var variant = new ShopifyProductVariantEntity
        {
            GlobalProductId = $"gid://shopify/Product/{owningProductId}",
            ProductId = owningProductId,
            GlobalVariantId = $"gid://shopify/ProductVariant/{variantId}",
            VariantId = variantId,
            Sku = $"SKU-{variantId}",
            Barcode = $"BAR-{variantId}",
            DisplayName = $"Variant {variantId}",
            IsActive = isActive,
            IsDeleted = isDeleted
        };

        if (skulabsSourceItemId is not null)
        {
            variant.SkulabsItemListings.Add(new SkulabsItemListingEntity
            {
                SkulabsSourceListingId = $"listing-{skulabsSourceItemId}",
                RawVariantId = variantId.ToString(),
                ShopifyProductId = owningProductId.ToString(),
                SkulabsItem = new SkulabsItemEntity
                {
                    SkulabsSourceItemId = skulabsSourceItemId,
                    Title = $"SkuLabs {skulabsSourceItemId}",
                    Sku = variant.Sku,
                    Barcode = variant.Barcode
                }
            });
        }

        dbContext.ShopifyProductVariants.Add(variant);
        await dbContext.SaveChangesAsync();

        return variantId;
    }

    /// <summary>
    /// Adds a second SkuLabs item whose only listing points at an already-linked variant, leaving two
    /// items contesting one variant. Neither link is usable, which is the state the cardinality guard
    /// exists to catch.
    /// </summary>
    public async Task SeedCompetingSkulabsItem(long variantId, string skulabsSourceItemId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var variant = await dbContext.ShopifyProductVariants
            .SingleAsync(entity => entity.VariantId == variantId);

        dbContext.SkulabsItems.Add(new SkulabsItemEntity
        {
            SkulabsSourceItemId = skulabsSourceItemId,
            Title = $"SkuLabs {skulabsSourceItemId}",
            Sku = variant.Sku,
            Barcode = variant.Barcode,
            Listings =
            {
                new SkulabsItemListingEntity
                {
                    SkulabsSourceListingId = $"listing-{skulabsSourceItemId}",
                    RawVariantId = variantId.ToString(),
                    ShopifyProductId = variant.ProductId.ToString(),
                    ShopifyProductVariantId = variant.ShopifyProductVariantId
                }
            }
        });

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds one SkuLabs item carrying a listing per supplied raw variant id. Each listing resolves to
    /// a seeded variant where one exists and stays unresolved otherwise. More than one listing makes
    /// the item ambiguous — a property of its listing count, not a flag.
    /// </summary>
    public async Task SeedSkulabsItemWithListings(string skulabsSourceItemId, params long[] rawVariantIds)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var variantsById = await dbContext.ShopifyProductVariants
            .Where(entity => rawVariantIds.Contains(entity.VariantId))
            .ToDictionaryAsync(entity => entity.VariantId, entity => entity.ShopifyProductVariantId);

        var item = new SkulabsItemEntity
        {
            SkulabsSourceItemId = skulabsSourceItemId,
            Title = $"SkuLabs {skulabsSourceItemId}",
            Sku = $"SKU-{skulabsSourceItemId}",
            Barcode = $"BAR-{skulabsSourceItemId}"
        };

        foreach (var rawVariantId in rawVariantIds)
        {
            item.Listings.Add(new SkulabsItemListingEntity
            {
                SkulabsSourceListingId = $"listing-{skulabsSourceItemId}-{rawVariantId}",
                RawVariantId = rawVariantId.ToString(),
                ShopifyProductId = (rawVariantId * 100).ToString(),
                ShopifyProductVariantId = variantsById.TryGetValue(rawVariantId, out var guid) ? guid : null
            });
        }

        dbContext.SkulabsItems.Add(item);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Removes every seeded variant and its linked SkuLabs item.</summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await dbContext.SkulabsItems.ExecuteDeleteAsync();
        await dbContext.ShopifyProductVariantLogEvents.ExecuteDeleteAsync();
        await dbContext.ShopifyProductVariants.ExecuteDeleteAsync();
    }

    public new async Task DisposeAsync()
    {
        RestoreEnvironmentVariables();
        await postgres.DisposeAsync();
        Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        originalEnvironmentValues[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private void RestoreEnvironmentVariables()
    {
        foreach (var (key, value) in originalEnvironmentValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
