using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class CSharpTaskConfiguration : IEntityTypeConfiguration<CSharpTask>
{
    /// <summary>Name used by the filtered unique index; the service guard is the primary rule.</summary>
    public const string OneActivePerMonthIndex = "IX_CSharpTasks_OneActivePerMonth";

    public void Configure(EntityTypeBuilder<CSharpTask> builder)
    {
        builder.Property(task => task.Title).IsRequired().HasMaxLength(120);
        builder.Property(task => task.ReferenceFolderPath).HasMaxLength(400);
        builder.Property(task => task.StarterZipPath).HasMaxLength(400);
        builder.Property(task => task.StarterZipFileName).HasMaxLength(200);

        // Every month lists its tasks in board-defined order.
        builder.HasIndex(task => new { task.MonthSheetId, task.SortOrder });

        // Same defence in depth as DS sheets: one featured task per month, enforced by SQL too.
        builder.HasIndex(task => task.MonthSheetId)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName(OneActivePerMonthIndex);

        builder.HasOne(task => task.MonthSheet)
            .WithMany()
            .HasForeignKey(task => task.MonthSheetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(task => task.CreatedBy)
            .WithMany()
            .HasForeignKey(task => task.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}