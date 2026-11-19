using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// One graded C# attempt (PLAN §11). The filtered folder and the generated Markdown bundle live
/// side by side on disk; this row keeps the counts, the grading state and the score history.
/// </summary>
public sealed class CSharpSubmission
{
    public CSharpSubmission()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Starts at 1 for each member and task; unique together with the pair.</summary>
    public int AttemptNo { get; set; }

    /// <summary>Path relative to <c>Uploads:Root</c>, e.g. <c>{userId}/{submissionId}</c>.</summary>
    public string StoredFolderPath { get; set; } = string.Empty;

    /// <summary>Path relative to <c>Uploads:Root</c>, e.g. <c>{userId}/{submissionId}/bundle.md</c>.</summary>
    public string BundlePath { get; set; } = string.Empty;

    public int FileCount { get; set; }

    /// <summary>Files that were sent but not kept: build output, non-allowlisted files, duplicates.</summary>
    public int IgnoredFileCount { get; set; }

    public long TotalBytes { get; set; }

    public int BundleChars { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Uploaded;

    /// <summary>The score that counts. Equal to <see cref="AiScore"/> until the board edits it.</summary>
    public int? Score { get; set; }

    /// <summary>The original AI value, kept so a manual edit is visible.</summary>
    public int? AiScore { get; set; }

    public bool IsManualScore { get; set; }

    public string? FeedbackJson { get; set; }

    /// <summary>One-line outcome for lists, denormalised from the structured feedback.</summary>
    public string? Summary { get; set; }

    public string? Verdict { get; set; }

    public bool IsLate { get; set; }

    public DateTimeOffset SubmittedAtUtc { get; set; }

    public DateTimeOffset? GradedAtUtc { get; set; }

    public string? GradingModel { get; set; }

    /// <summary>SHA-256 of the exact prompt sent, for auditing and duplicate detection.</summary>
    public string? PromptHash { get; set; }

    /// <summary>Version of the board-owned grading prompt used for this attempt.</summary>
    public int? PromptVersion { get; set; }

    /// <summary>Characters of submission text actually sent (after truncation and redaction).</summary>
    public int? InputChars { get; set; }

    /// <summary>SHA-256 of the stored bundle, so identical uploads are not graded twice.</summary>
    public string? BundleHash { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public int RetryCount { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Guards two board members editing the same score at once.</summary>
    public byte[] RowVersion { get; set; } = [];

    public CSharpTask? Task { get; set; }

    public ApplicationUser? User { get; set; }
}