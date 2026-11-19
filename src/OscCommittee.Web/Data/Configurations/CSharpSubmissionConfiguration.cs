using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OscCommittee.Web.Domain.Entities;

namespace OscCommittee.Web.Data.Configurations;

public sealed class CSharpSubmissionConfiguration : IEntityTypeConfiguration<CSharpSubmission>
{
    /// <summary>Named so an attempt-number race can be told apart from a real failure.</summary>
    public const string AttemptIndex = "IX_CSharpSubmissions_Task_User_AttemptNo";

    public void Configure(EntityTypeBuilder<CSharpSubmission> builder)
    {
        builder.Property(submission => submission.StoredFolderPath).IsRequired().HasMaxLength(300);
        builder.Property(submission => submission.BundlePath).IsRequired().HasMaxLength(300);

        // Summary and Verdict are intentionally uncapped (nvarchar(max)): the AI writes the
        // review as one piece, and the old 600/40 caps cut sentences off mid-word.
        builder.Property(submission => submission.GradingModel).HasMaxLength(100);
        builder.Property(submission => submission.PromptHash).HasMaxLength(64);
        builder.Property(submission => submission.BundleHash).HasMaxLength(64);
        builder.Property(submission => submission.ErrorMessage).HasMaxLength(1000);

        builder.Property(submission => submission.RowVersion).IsRowVersion();

        // One attempt number per member and task; the service computes the next value.
        builder.HasIndex(submission => new { submission.TaskId, submission.UserId, submission.AttemptNo })
            .IsUnique()
            .HasDatabaseName(AttemptIndex);

        // The attempts table for a task and a member's "my attempts" history.
        builder.HasIndex(submission => new { submission.TaskId, submission.UserId });
        builder.HasIndex(submission => new { submission.UserId, submission.SubmittedAtUtc });

        // The grading queue drains by status.
        builder.HasIndex(submission => submission.Status);

        builder.HasOne(submission => submission.Task)
            .WithMany()
            .HasForeignKey(submission => submission.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(submission => submission.User)
            .WithMany()
            .HasForeignKey(submission => submission.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}