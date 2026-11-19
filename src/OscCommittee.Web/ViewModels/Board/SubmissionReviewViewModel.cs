using System.ComponentModel.DataAnnotations;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.ViewModels.Tasks;

namespace OscCommittee.Web.ViewModels.Board;

/// <summary>One attempt in the board submissions table (PLAN §8.7).</summary>
public sealed class BoardSubmissionAttemptViewModel
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public string MemberName { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    public int AttemptNumber { get; init; }

    public string SubmittedLabel { get; init; } = string.Empty;

    public int FileCount { get; init; }

    public bool IsLate { get; init; }

    public SubmissionStatus Status { get; init; }

    public int? Score { get; init; }

    public int? Points { get; init; }

    public bool IsBest { get; init; }

    public string StatusLabel => SubmissionStatusView.Label(Status);

    public string StatusBadgeClass => SubmissionStatusView.BadgeClass(Status);

    public string ScoreLabel => Score?.ToString() ?? "—";

    public string PointsLabel => Points?.ToString() ?? "—";
}

public sealed class CSharpTaskSubmissionsViewModel
{
    public Guid TaskId { get; init; }

    public string TaskTitle { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public bool IsHidden { get; init; }

    public IReadOnlyList<BoardSubmissionAttemptViewModel> Attempts { get; init; } = [];

    public string Sort { get; init; } = "submitted";

    public string StatusFilter { get; init; } = "all";

    public string? Query { get; init; }

    public int TotalCount { get; init; }

    public int GradedCount { get; init; }

    public int PendingCount { get; init; }

    public int FailedCount { get; init; }

    public int LateCount { get; init; }

    public int MemberCount { get; init; }

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Query) || StatusFilter != "all" || Sort != "submitted";
}

/// <summary>The board review page: statement and solution fold away, files and score stay visible.</summary>
public sealed class BoardSubmissionReviewViewModel
{
    public Guid Id { get; init; }

    public Guid TaskId { get; init; }

    public string TaskTitle { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string MemberName { get; init; } = string.Empty;

    public string MemberEmail { get; init; } = string.Empty;

    public string MemberInitials { get; init; } = string.Empty;

    public int AttemptNumber { get; init; }

    public string SubmittedLabel { get; init; } = string.Empty;

    public string? GradedLabel { get; init; }

    public bool IsLate { get; init; }

    public SubmissionStatus Status { get; init; }

    public int FileCount { get; init; }

    public int IgnoredFileCount { get; init; }

    public string TotalSizeLabel { get; init; } = string.Empty;

    public int BundleChars { get; init; }

    public int? Score { get; init; }

    public int? AiScore { get; init; }

    /// <summary>Upper bound for a manual score; mirrors the grading constant.</summary>
    public int MaxPoints { get; init; } = SubmissionScoring.MaxPoints;

    public bool IsManualScore { get; init; }

    public string? Summary { get; init; }

    public string? Verdict { get; init; }

    public string? ErrorMessage { get; init; }

    public string? GradingModel { get; init; }

    public int? PromptVersion { get; init; }

    public string? PromptHashShort { get; init; }

    public int? InputChars { get; init; }

    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    public int RetryCount { get; init; }

    public string RowVersion { get; init; } = string.Empty;

    public bool GradingConfigured { get; init; }

    public string StatementHtml { get; init; } = string.Empty;

    public bool HasStatement { get; init; }

    public bool HasReference { get; init; }

    public string? ReferenceFolderPath { get; init; }

    public SubmissionFilesView? ReferenceFiles { get; init; }

    public GradingFeedback? Feedback { get; init; }

    public IReadOnlyList<SubmissionFile> Files { get; init; } = [];

    public string? SelectedPath { get; init; }

    public string? SelectedContent { get; init; }

    public string SelectedLanguage { get; init; } = "csharp";

    public bool SelectedTruncated { get; init; }

    public string? FileError { get; init; }

    public string StatusLabel => SubmissionStatusView.Label(Status);

    public string StatusBadgeClass => SubmissionStatusView.BadgeClass(Status);

    public bool IsPending => Status is SubmissionStatus.Uploaded
        or SubmissionStatus.Grading
        or SubmissionStatus.AwaitingReview;

    public bool IsFailed => Status == SubmissionStatus.Failed;

    public bool IsGraded => Status == SubmissionStatus.Graded;

    /// <summary>An AI draft the board still has to accept.</summary>
    public bool IsAwaitingReview => Status == SubmissionStatus.AwaitingReview;

    public string ScoreLabel => Score?.ToString() ?? "—";

    public string AiScoreLabel => AiScore?.ToString() ?? "—";

    public string PointsLabel =>
        Score is null
            ? "—"
            : (IsManualScore
                ? Math.Clamp(Score.Value, 1, 5)
                : IsLate ? 0 : Math.Clamp(Score.Value, 1, 5)).ToString();

    public bool HasFeedback => Feedback is not null;

    public IReadOnlyList<string> Strengths => Feedback?.Strengths ?? [];

    public IReadOnlyList<SubmissionIssueViewModel> Issues =>
        (Feedback?.Issues ?? [])
            .Select(issue => new SubmissionIssueViewModel
            {
                Severity = issue.Severity,
                File = issue.File,
                Message = issue.Message
            })
            .ToList();

    public IReadOnlyList<SubmissionIssueViewModel> MustFix =>
        Issues.Where(issue => issue.Severity == GradingIssueSeverity.MustFix).ToList();

    public IReadOnlyList<SubmissionIssueViewModel> ShouldFix =>
        Issues.Where(issue => issue.Severity == GradingIssueSeverity.ShouldFix).ToList();

    public IReadOnlyList<SubmissionIssueViewModel> Nits =>
        Issues.Where(issue => issue.Severity == GradingIssueSeverity.Nit).ToList();

    public string FileSizeLabel(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };
}

public sealed class BoardSubmissionScoreForm
{
    [Range(1, 5, ErrorMessage = "Enter a whole number from 1 to 5.")]
    public int Score { get; set; }

    /// <summary>The board may rewrite the AI summary; it is stored in full with the accepted grade.</summary>
    public string? Summary { get; set; }

    public string RowVersion { get; set; } = string.Empty;
}