using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Tasks;

namespace OscCommittee.Web.ViewModels.Tasks;

/// <summary>Human wording and badge styling for a submission state.</summary>
public static class SubmissionStatusView
{
    public static string Label(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Uploaded => "Awaiting grading",
        SubmissionStatus.Grading => "Grading",
        SubmissionStatus.AwaitingReview => "Awaiting review",
        SubmissionStatus.Graded => "Graded",
        SubmissionStatus.Failed => "Grading failed",
        SubmissionStatus.TryAgain => "Try again",
        _ => status.ToString()
    };

    public static string BadgeClass(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Graded => "badge-graded",
        SubmissionStatus.Failed => "badge-late",
        SubmissionStatus.Grading => "badge-active",
        SubmissionStatus.TryAgain => "badge-late",
        _ => "badge-pending"
    };
}

/// <summary>One attempt in the member or board table.</summary>
public sealed class TaskAttemptRowViewModel
{
    public Guid Id { get; init; }

    public int AttemptNumber { get; init; }

    public string SubmittedLabel { get; init; } = string.Empty;

    /// <summary>Compact timestamp for the narrow attempts list on the task page.</summary>
    public string SubmittedShortLabel { get; init; } = string.Empty;

    public int FileCount { get; init; }

    public bool IsLate { get; init; }

    public SubmissionStatus Status { get; init; }

    public int? Score { get; init; }

    /// <summary>The AI's draft score, shown until the board accepts it.</summary>
    public int? AiScore { get; init; }

    public int? Points { get; init; }

    public bool IsBest { get; init; }

    public bool HasFeedback { get; init; }

    /// <summary>The stored summary, used when a syntax error explains itself on the task page.</summary>
    public string? Summary { get; init; }

    /// <summary>True while only the AI draft exists; nothing has been credited yet.</summary>
    public bool IsDraftScore => Score is null && AiScore is not null;

    public string StatusLabel => SubmissionStatusView.Label(Status);

    public string StatusBadgeClass => SubmissionStatusView.BadgeClass(Status);

    public string ScoreLabel => (Score ?? AiScore)?.ToString() ?? "—";

    public string PointsLabel => Points?.ToString() ?? "—";
}

/// <summary>One task in the member list.</summary>
public sealed class TaskRowViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public string DueIso { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool IsOverdue { get; init; }

    public int MaxPoints { get; init; }

    public int AttemptCount { get; init; }

    public int? BestScore { get; init; }

    public SubmissionStatus? LatestStatus { get; init; }

    public string StateLabel => LatestStatus switch
    {
        SubmissionStatus.Graded => "Graded",
        SubmissionStatus.Uploaded or SubmissionStatus.Grading or SubmissionStatus.AwaitingReview => "Submitted",
        SubmissionStatus.TryAgain => "Try again",
        SubmissionStatus.Failed => "Grading failed",
        _ => IsOverdue ? "Late" : "Available"
    };

    public string StateBadgeClass => LatestStatus switch
    {
        SubmissionStatus.Graded => "badge-graded",
        SubmissionStatus.Uploaded or SubmissionStatus.Grading or SubmissionStatus.AwaitingReview => "badge-active",
        SubmissionStatus.TryAgain => "badge-late",
        SubmissionStatus.Failed => "badge-late",
        _ => IsOverdue ? "badge-late" : "badge-active"
    };

    public string BestScoreLabel => BestScore?.ToString() ?? "—";

    public string AttemptsLabel => AttemptCount switch
    {
        0 => "No attempts yet",
        1 => "1 attempt",
        _ => $"{AttemptCount} attempts"
    };
}

public sealed class TaskMonthGroupViewModel
{
    public int Year { get; init; }

    public int Month { get; init; }

    public string Label { get; init; } = string.Empty;

    public bool IsCurrent { get; init; }

    public IReadOnlyList<TaskRowViewModel> Tasks { get; init; } = [];
}

public sealed class TasksViewModel
{
    public string MonthLabel { get; init; } = string.Empty;

    public TaskRowViewModel? Active { get; init; }

    public IReadOnlyList<TaskMonthGroupViewModel> Groups { get; init; } = [];

    public int TaskCount => Groups.Sum(group => group.Tasks.Count);
}

public sealed class TaskPageViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public string DueIso { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool IsOverdue { get; init; }

    public string StatementHtml { get; init; } = string.Empty;

    public bool HasStatement { get; init; }

    public int MaxPoints { get; init; }

    public int MaxFiles { get; init; }

    public int MaxTotalMb { get; init; }

    public int MaxFileMb { get; init; }

    public bool HasStarterZip { get; init; }

    public string? StarterZipFileName { get; init; }

    public long StarterZipSizeBytes { get; init; }

    public bool HasReference { get; init; }

    /// <summary>True once the deadline has passed: the reference is shared automatically.</summary>
    public bool ShowReference { get; init; }

    public SubmissionFilesView? ReferenceFiles { get; init; }

    public IReadOnlyList<TaskAttemptRowViewModel> Attempts { get; init; } = [];

    public int? BestScore { get; init; }

    public int? BestPoints { get; init; }

    public SubmissionStatus? LatestStatus { get; init; }

    /// <summary>The newest attempt; only one normal submission per member is allowed.</summary>
    public Guid? LatestAttemptId { get; init; }

    /// <summary>True while the board still has to accept (or reject) the submission.</summary>
    public bool IsPending => LatestStatus is SubmissionStatus.Uploaded or SubmissionStatus.Grading;

    /// <summary>The AI found a syntax error; the member may upload a corrected folder.</summary>
    public bool IsTryAgain => LatestStatus == SubmissionStatus.TryAgain;

    /// <summary>False once a normal submission exists: one attempt per task, except syntax errors.</summary>
    public bool CanSubmit { get; init; }

    public TaskAttemptRowViewModel? Latest => Attempts.FirstOrDefault();

    public bool HasAttempts => Attempts.Count > 0;

    public string StateLabel => LatestStatus switch
    {
        SubmissionStatus.Graded => "Graded",
        SubmissionStatus.Uploaded or SubmissionStatus.Grading or SubmissionStatus.AwaitingReview => "Submitted",
        SubmissionStatus.TryAgain => "Try again",
        SubmissionStatus.Failed => "Grading failed",
        _ => IsOverdue ? "Late" : "Available"
    };

    public string StateBadgeClass => LatestStatus switch
    {
        SubmissionStatus.Graded => "badge-graded",
        SubmissionStatus.Uploaded or SubmissionStatus.Grading or SubmissionStatus.AwaitingReview => "badge-active",
        SubmissionStatus.TryAgain => "badge-late",
        SubmissionStatus.Failed => "badge-late",
        _ => IsOverdue ? "badge-late" : "badge-active"
    };

    public string BestScoreLabel => BestScore?.ToString() ?? "—";
}

/// <summary>One issue line rendered with its severity badge.</summary>
public sealed class SubmissionIssueViewModel
{
    public GradingIssueSeverity Severity { get; init; }

    public string File { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string SeverityLabel => GradingSeverityView.Label(Severity);

    public string SeverityBadgeClass => GradingSeverityView.BadgeClass(Severity);
}

/// <summary>The submission page and its "grading in progress" state.</summary>
public sealed class SubmissionPageViewModel
{
    public Guid Id { get; init; }

    public Guid TaskId { get; init; }

    public string TaskTitle { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public int AttemptNumber { get; init; }

    public string SubmittedLabel { get; init; } = string.Empty;

    public bool IsLate { get; init; }

    public SubmissionStatus Status { get; init; }

    public int? Score { get; init; }

    public int? Points { get; init; }

    public string? Summary { get; init; }

    public string? Verdict { get; init; }

    public string? ErrorMessage { get; init; }

    public bool GradingConfigured { get; init; }

    public int MaxPoints { get; init; } = Services.Tasks.SubmissionScoring.MaxPoints;

    public GradingFeedback? Feedback { get; init; }

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

    public bool IsPending => Status is SubmissionStatus.Uploaded
        or SubmissionStatus.Grading
        or SubmissionStatus.AwaitingReview;

    /// <summary>True while the attempt is a syntax-error marker the member can replace.</summary>
    public bool IsTryAgain => Status == SubmissionStatus.TryAgain;

    /// <summary>True while the AI draft exists but the board has not accepted it yet.</summary>
    public bool IsAwaitingReview => Status == SubmissionStatus.AwaitingReview;

    /// <summary>The draft score shown before acceptance; not credited and not final.</summary>
    public int? DraftScore => Feedback?.Score;

    public bool IsFailed => Status == SubmissionStatus.Failed;

    public bool IsGraded => Status == SubmissionStatus.Graded;

    public string StatusLabel => SubmissionStatusView.Label(Status);

    public string StatusBadgeClass => SubmissionStatusView.BadgeClass(Status);

    public string ScoreLabel => Score?.ToString() ?? "—";

    public string PointsLabel => Points?.ToString() ?? "—";
}