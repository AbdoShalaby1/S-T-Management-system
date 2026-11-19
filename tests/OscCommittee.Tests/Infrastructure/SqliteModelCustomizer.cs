using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OscCommittee.Tests.Infrastructure;

/// <summary>
/// SQLite needs two adjustments for SQL Server-shaped entities:
/// <list type="bullet">
/// <item><c>rowversion</c>: SQL Server generates the value on insert/update; SQLite would insert
/// NULL into a NOT NULL column. Keep the concurrency token but let EF send the entity's value.</item>
/// <item><c>DateTimeOffset ORDER BY</c>: SQLite cannot sort the provider's default text mapping.
/// Store UTC ticks instead so ordering and comparisons match SQL Server semantics. The app only
/// ever writes UTC values, so the lost offset is not observable.</item>
/// </list>
/// </summary>
public sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter = new(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate)
                {
                    property.ValueGenerated = ValueGenerated.Never;
                }

                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (clrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(UtcTicksConverter);
                }
            }
        }
    }
}