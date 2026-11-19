using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Web.Services.Tasks;

/// <summary>What the upload page needs after a successful save.</summary>
public sealed record SubmissionReceipt(
    Guid SubmissionId,
    Guid TaskId,
    int AttemptNo,
    bool IsLate,
    int KeptFiles,
    int IgnoredFiles,
    int DuplicateFiles,
    int BundleChars);

/// <summary>One row of the member or board attempts table.</summary>
public sealed record SubmissionAttemptRow(
    Guid Id,
    int AttemptNo,
    DateTimeOffset SubmittedAtUtc,
    int FileCount,
    bool IsLate,
    SubmissionStatus Status,
    int? Score,
    int? AiScore,
    int? Points,
    string? Summary,
    string? Verdict,
    bool IsBest);

/// <summary>The submission page plus everything the polling stub reads.</summary>
public sealed record SubmissionDetail(
    Guid Id,
    Guid TaskId,
    string TaskTitle,
    string MonthLabel,
    int AttemptNo,
    DateTimeOffset SubmittedAtUtc,
    bool IsLate,
    SubmissionStatus Status,
    int? Score,
    int? Points,
    string? Summary,
    string? Verdict,
    string? ErrorMessage,
    DateTimeOffset? GradedAtUtc,
    bool HasFeedback,
    GradingFeedback? Feedback);

public sealed record SubmissionStatusSnapshot(
    Guid Id,
    SubmissionStatus Status,
    int? Score,
    int? Points,
    bool IsLate,
    bool GradingConfigured,
    string? ErrorMessage,
    DateTimeOffset? GradedAtUtc);

public sealed record BoardSubmissionRow(
    Guid Id,
    Guid UserId,
    string MemberName,
    int AttemptNo,
    DateTimeOffset SubmittedAtUtc,
    int FileCount,
    bool IsLate,
    SubmissionStatus Status,
    int? Score,
    int? Points,
    bool IsBest);

/// <summary>A compact per-task roll-up for the member task list.</summary>
public sealed record TaskAttemptSummary(
    Guid TaskId,
    int AttemptCount,
    int? BestScore,
    int? BestPoints,
    SubmissionStatus LatestStatus);

/// <summary>Shared score-to-points rule (PLAN §7.3): late work is graded but worth 0.</summary>
public static class SubmissionScoring
{
    public const int MinimumScore = 1;

    public const int MaxPoints = 5;

    public static int? Points(int? score, bool isLate) =>
        score is null ? null : isLate ? 0 : Math.Clamp(score.Value, 1, MaxPoints);

    /// <summary>
    /// A board-edited score is authoritative (PLAN §6.7): it counts even when the attempt is late.
    /// </summary>
    public static int? EffectivePoints(int? score, bool isLate, bool isManualScore) =>
        score is null
            ? null
            : isManualScore
                ? Math.Clamp(score.Value, MinimumScore, MaxPoints)
                : Points(score, isLate);
}

/// <summary>
/// Upload pipeline (PLAN §10.4): validate, store the filtered folder, build the Markdown bundle,
/// and record the attempt. Grading itself starts in Part 10.
/// </summary>
public interface ICSharpSubmissionService
{
    Task<Result<SubmissionReceipt>> SubmitAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<SubmissionInputFile> files,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionAttemptRow>> GetAttemptsAsync(
        Guid taskId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubmissionDetail?> FindForMemberAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubmissionStatusSnapshot?> GetStatusAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskAttemptSummary>> GetTaskSummariesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BoardSubmissionRow>> GetBoardAttemptsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);
}