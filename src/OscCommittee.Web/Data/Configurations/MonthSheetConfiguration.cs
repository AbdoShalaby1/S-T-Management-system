using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class MonthSheetConfiguration : IEntityTypeConfiguration<MonthSheet>
{
    public void Configure(EntityTypeBuilder<MonthSheet> builder)
    {
        builder.Property(sheet => sheet.Status)
            .HasConversion<int>()
            .IsRequired();

        // One sheet per calendar month, enforced by the database as well as the service.
        builder.HasIndex(sheet => new { sheet.Year, sheet.Month }).IsUnique();

        builder.Property(sheet => sheet.RowVersion).IsRowVersion();
    }
}
