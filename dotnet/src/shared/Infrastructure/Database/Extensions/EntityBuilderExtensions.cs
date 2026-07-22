using System.Linq.Expressions;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// Extension methods for EF Core's <see cref="EntityTypeBuilder{TEntity}"/> and property
/// builders that encapsulate common PostgreSQL-specific conventions used across entity
/// configurations (UUIDv7 primary keys, UTC timestamp defaults, etc.).
/// </summary>
internal static class EntityBuilderExtensions
{
    extension<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        /// <summary>
        /// Configures <paramref name="keyExpression"/> as the primary key and instructs EF Core
        /// to never generate the value on the .NET side (<c>ValueGeneratedNever</c>), relying
        /// instead on the PostgreSQL <c>uuidv7()</c> default for database-generated inserts.
        /// </summary>
        /// <param name="keyExpression">The property expression that identifies the primary key column.</param>
        /// <returns>The configured <see cref="KeyBuilder"/>.</returns>
        public KeyBuilder HasUuidV7PrimaryKey(Expression<Func<TEntity, object?>> keyExpression)
        {
            builder.Property(keyExpression).ValueGeneratedNever().HasDefaultValueSql("uuidv7()");

            return builder.HasKey(keyExpression);
        }
    }

    extension(PropertyBuilder<DateTime> builder)
    {
        /// <summary>
        /// Sets the SQL default for this <see cref="DateTime"/> column to PostgreSQL's
        /// <c>'-infinity'::timestamp</c>, representing the earliest possible timestamp.
        /// </summary>
        public PropertyBuilder<DateTime> HasDefaultValueDateTimeMinSql()
        {
            return builder.HasDefaultValueSql("'-infinity'::timestamp");
        }

        /// <summary>
        /// Sets the SQL default for this <see cref="DateTime"/> column to
        /// <c>now() at time zone 'utc'</c>, producing the current UTC timestamp on insert.
        /// </summary>
        public PropertyBuilder<DateTime> HasDefaultValueDateTimeNowUtcSql()
        {
            return builder.HasDefaultValueSql("now() at time zone 'utc'");
        }
    }

    extension(PropertyBuilder<Guid> builder)
    {
        /// <summary>
        /// Sets the SQL default for this <see cref="Guid"/> column to the all-zeros UUID
        /// <c>'00000000-0000-0000-0000-000000000000'</c>.
        /// </summary>
        public PropertyBuilder<Guid> HasDefaultValueEmptyGuidSql()
        {
            return builder.HasDefaultValueSql("'00000000-0000-0000-0000-000000000000'");
        }

        /// <summary>
        /// Sets the SQL default for this <see cref="Guid"/> column to <c>uuidv7()</c>,
        /// generating a time-ordered UUID v7 on the database side during insert.
        /// </summary>
        public PropertyBuilder<Guid> HasDefaultValueGuidV7Sql()
        {
            return builder.HasDefaultValueSql("uuidv7()");
        }
    }
}