using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database;

/// <summary>
/// The primary Entity Framework Core database context for SkuSync. Exposes typed
/// <see cref="DbSet{TEntity}"/> properties for every aggregate stored in the database
/// and applies all <see cref="IEntityTypeConfiguration{TEntity}"/> configurations found
/// in this assembly.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets the set of Shopify product variant records persisted in the database.
    /// </summary>
    public DbSet<ShopifyProductVariantEntity> ShopifyProductVariants { get; init; }

    /// <summary>
    /// Gets the set of log events recording changes to Shopify product variant records.
    /// </summary>
    public DbSet<ShopifyProductVariantLogEventEntity> ShopifyProductVariantLogEvents { get; init; }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
