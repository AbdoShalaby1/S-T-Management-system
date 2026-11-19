using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class DsProblemConfiguration : IEntityTypeConfiguration<DsProblem>
{
    /// <summary>Named so the service can report a duplicate import without leaking SQL details.</summary>
    public const string SheetProblemIndex = "IX_DsProblems_DsSheet_Platform_ExternalId";

    public void Configure(EntityTypeBuilder<DsProblem> builder)
    {
        builder.Property(problem => problem.ExternalId).IsRequired().HasMaxLength(64);
        builder.Property(problem => problem.Title).IsRequired().HasMaxLength(200);
        builder.Property(problem => problem.Url).IsRequired().HasMaxLength(400);

        // One copy of a platform problem per sheet.
        builder.HasIndex(problem => new { problem.DsSheetId, problem.Platform, problem.ExternalId })
            .IsUnique()
            .HasDatabaseName(SheetProblemIndex);

        // The sync engine looks problems up by platform id alone.
        builder.HasIndex(problem => new { problem.Platform, problem.ExternalId });

        // Sheet listing order.
        builder.HasIndex(problem => new { problem.DsSheetId, problem.SortOrder });

        builder.HasOne(problem => problem.DsSheet)
            .WithMany()
            .HasForeignKey(problem => problem.DsSheetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}