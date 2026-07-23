using Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Database;

/// <summary>
/// Save helpers that make <see cref="ShopifyProductVariantEntity"/> inserts tolerant of the
/// read-then-insert race shared by the maintenance import and the product webhook handlers:
/// each reads a snapshot of existing variants and then inserts the ones it considers new, but a
/// concurrent writer can commit a variant with the same GlobalVariantId/VariantId in between.
/// </summary>
public static class ShopifyProductVariantSaveExtensions
{
    // Postgres unique_violation. Raised when a concurrent writer has already committed a row
    // whose GlobalVariantId/VariantId collides with one we are about to insert.
    private const string UniqueViolationSqlState = "23505";

    // One extra attempt covers the common single-collision case; the cap stops a pathological
    // stream of concurrent inserts from livelocking the save. On exhaustion the original
    // DbUpdateException propagates so the caller's existing handling (Quartz failure result /
    // SQS redelivery) still applies.
    private const int MaxAttempts = 3;

    /// <summary>
    /// Saves pending changes, tolerating the race in which a concurrent writer has committed a
    /// <see cref="ShopifyProductVariantEntity"/> bearing a GlobalVariantId/VariantId this context
    /// is about to insert. On such a unique-constraint violation the offending pending inserts are
    /// dropped — the row already exists, so re-inserting it is redundant — and the save is retried.
    /// Any violation that can't be resolved by dropping a duplicate insert (e.g. a SKU clash) is
    /// rethrown unchanged.
    /// </summary>
    /// <returns>
    /// The variant inserts that were dropped because a concurrent writer had already committed
    /// them. Callers should exclude these from any post-save event publishing so no phantom
    /// "variant created" events are emitted for rows this context never wrote.
    /// </returns>
    public static async Task<IReadOnlySet<ShopifyProductVariantEntity>> SaveChangesToleratingVariantConflicts(
        this ApplicationDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var dropped = new HashSet<ShopifyProductVariantEntity>();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return dropped;
            }
            catch (DbUpdateException exception)
                when (IsUniqueViolation(exception) && attempt < MaxAttempts)
            {
                var droppedThisAttempt = await DropConflictingVariantInserts(dbContext, cancellationToken);
                if (droppedThisAttempt.Count == 0)
                {
                    // Nothing we can resolve by dropping a duplicate insert — surface the
                    // original violation instead of spinning until the attempt cap.
                    throw;
                }

                dropped.UnionWith(droppedThisAttempt);
                logger.LogWarning(exception,
                    "A concurrent writer had already committed {Count} of the variants this save tried to insert; dropped them and retrying (attempt {Attempt}).",
                    droppedThisAttempt.Count, attempt);
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState };

    /// <summary>
    /// Detaches every pending variant insert whose GlobalVariantId or VariantId already exists in
    /// the database, so a retried save no longer collides with the concurrently-committed row.
    /// </summary>
    private static async Task<IReadOnlyCollection<ShopifyProductVariantEntity>> DropConflictingVariantInserts(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var pendingInserts = dbContext.ChangeTracker
            .Entries<ShopifyProductVariantEntity>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        if (pendingInserts.Length == 0)
        {
            return [];
        }

        var pendingGlobalVariantIds = pendingInserts.Select(v => v.GlobalVariantId).ToArray();
        var pendingVariantIds = pendingInserts.Select(v => v.VariantId).ToArray();

        // IgnoreQueryFilters so a row the other writer deactivated still counts as committed.
        var committedGlobalVariantIds = await dbContext.ShopifyProductVariants
            .IgnoreQueryFilters()
            .Where(v => pendingGlobalVariantIds.Contains(v.GlobalVariantId))
            .Select(v => v.GlobalVariantId)
            .ToHashSetAsync(cancellationToken);

        var committedVariantIds = await dbContext.ShopifyProductVariants
            .IgnoreQueryFilters()
            .Where(v => pendingVariantIds.Contains(v.VariantId))
            .Select(v => v.VariantId)
            .ToHashSetAsync(cancellationToken);

        var dropped = new List<ShopifyProductVariantEntity>();
        foreach (var variant in pendingInserts)
        {
            if (!committedGlobalVariantIds.Contains(variant.GlobalVariantId)
                && !committedVariantIds.Contains(variant.VariantId))
            {
                continue;
            }

            DetachVariantInsert(dbContext, variant);
            dropped.Add(variant);
        }

        return dropped;
    }

    private static void DetachVariantInsert(ApplicationDbContext dbContext, ShopifyProductVariantEntity variant)
    {
        // Detach the child log events first; left tracked as Added they would try to insert with
        // a foreign key pointing at the now-detached variant.
        foreach (var logEvent in variant.LogEvents.ToArray())
        {
            dbContext.Entry(logEvent).State = EntityState.Detached;
        }

        dbContext.Entry(variant).State = EntityState.Detached;
    }
}
