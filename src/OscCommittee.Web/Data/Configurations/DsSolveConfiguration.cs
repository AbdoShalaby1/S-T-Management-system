using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class DsSolveConfiguration : IEntityTypeConfiguration<DsSolve>
{
    /// <summary>Named so a duplicate credit can be told apart from a real failure.</summary>
    public const string ProblemIndex = "IX_DsSolves_User_ProblemId";

    public void Configure(EntityTypeBuilder<DsSolve> builder)
    {
        builder.Property(solve => solve.ExternalId).IsRequired().HasMaxLength(64);
        builder.Property(solve => solve.PlatformSubmissionId).IsRequired().HasMaxLength(64);
        builder.Property(solve => solve.PlatformUrl).IsRequired().HasMaxLength(400);
        builder.Property(solve => solve.Language).HasMaxLength(64);

        // A sheet problem earns points once per member. The same platform problem may sit on
        // several sheets, and each copy is credited separately.
        builder.HasIndex(solve => new { solve.UserId, solve.DsProblemId })
            .IsUnique()
            .HasDatabaseName(ProblemIndex);

        // Solver lists and "has solves" guards read by problem.
        builder.HasIndex(solve => solve.DsProblemId);

        builder.HasOne(solve => solve.User)
            .WithMany()
            .HasForeignKey(solve => solve.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a problem with credited solves is refused in the service; the database backs
        // that up instead of silently removing history.
        builder.HasOne(solve => solve.DsProblem)
            .WithMany()
            .HasForeignKey(solve => solve.DsProblemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}