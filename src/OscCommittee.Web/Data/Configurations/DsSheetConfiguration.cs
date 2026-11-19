using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class DsSheetConfiguration : IEntityTypeConfiguration<DsSheet>
{
    /// <summary>Name used by the filtered unique index; the service guard is the primary rule.</summary>
    public const string OneActivePerMonthIndex = "IX_DsSheets_OneActivePerMonth";

    public void Configure(EntityTypeBuilder<DsSheet> builder)
    {
        builder.Property(sheet => sheet.Title).IsRequired().HasMaxLength(120);

        // Every month lists its sheets in board-defined order.
        builder.HasIndex(sheet => new { sheet.MonthSheetId, sheet.SortOrder });

        // Defence in depth: the service swaps the active sheet in one SaveChanges, and the
        // database refuses a second active row if that logic is ever bypassed.
        builder.HasIndex(sheet => sheet.MonthSheetId)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName(OneActivePerMonthIndex);

        builder.HasOne(sheet => sheet.MonthSheet)
            .WithMany()
            .HasForeignKey(sheet => sheet.MonthSheetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sheet => sheet.CreatedBy)
            .WithMany()
            .HasForeignKey(sheet => sheet.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}