using System.Linq.Expressions;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Metadata.Builders;

internal static class EntityBuilderExtensions
{
    extension<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        public KeyBuilder HasUuidV7PrimaryKey(Expression<Func<TEntity, object?>> keyExpression)
        {
            builder.Property(keyExpression).ValueGeneratedNever().HasDefaultValueSql("uuidv7()");

            return builder.HasKey(keyExpression);
        }
    }

    extension(PropertyBuilder<DateTime> builder)
    {
        public PropertyBuilder<DateTime> HasDefaultValueDateTimeMinSql()
        {
            return builder.HasDefaultValueSql("'-infinity'::timestamp");
        }

        public PropertyBuilder<DateTime> HasDefaultValueDateTimeNowUtcSql()
        {
            return builder.HasDefaultValueSql("now() at time zone 'utc'");
        }
    }

    extension(PropertyBuilder<Guid> builder)
    {
        public PropertyBuilder<Guid> HasDefaultValueEmptyGuidSql()
        {
            return builder.HasDefaultValueSql("'00000000-0000-0000-0000-000000000000'");
        }

        public PropertyBuilder<Guid> HasDefaultValueGuidV7Sql()
        {
            return builder.HasDefaultValueSql("uuidv7()");
        }
    }
}