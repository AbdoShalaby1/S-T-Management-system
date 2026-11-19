using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class PointEntryConfiguration : IEntityTypeConfiguration<PointEntry>
{
    public const string SourceIndex = "IX_PointEntries_Category_SourceId";

    public void Configure(EntityTypeBuilder<PointEntry> builder)
    {
        builder.Property(entry => entry.Category).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(400);

        // One entry per solve/submission: makes sync and grading idempotent (PLAN §11).
        builder.HasIndex(entry => new { entry.Category, entry.SourceId })
            .IsUnique()
            .HasFilter("[SourceId] IS NOT NULL")
            .HasDatabaseName(SourceIndex);

        // Every total filters by month and member.
        builder.HasIndex(entry => new { entry.MonthSheetId, entry.UserId });

        builder.HasOne(entry => entry.User)
            .WithMany()
            .HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(entry => entry.MonthSheet)
            .WithMany()
            .HasForeignKey(entry => entry.MonthSheetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}