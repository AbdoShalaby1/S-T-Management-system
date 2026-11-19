using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.ViewModels.Tasks;

namespace OscCommittee.Web.Services.Tasks;

/// <summary>Everything the board review screen shows for one attempt.</summary>
public sealed record BoardSubmissionDetail(
    Guid Id,
    Guid TaskId,
    Guid UserId,
    string TaskTitle,
    string MonthLabel,
    string MemberName,
    string MemberEmail,
    int AttemptNo,
    DateTimeOffset SubmittedAtUtc,
    bool IsLate,
    int FileCount,
    int IgnoredFileCount,
    long TotalBytes,
    int BundleChars,
    SubmissionStatus Status,
    int? Score,
    int? AiScore,
    bool IsManualScore,
    string? Summary,
    string? Verdict,
    string? ErrorMessage,
    DateTimeOffset? GradedAtUtc,
    string? GradingModel,
    int? PromptVersion,
    string? PromptHash,
    int? InputChars,
    int? InputTokens,
    int? OutputTokens,
    int RetryCount,
    GradingFeedback? Feedback,
    string RowVersion,
    string? StatementMd,
    string? ReferenceFolderPath,
    int ReferenceFileCount,
    bool GradingConfigured);

/// <summary>Board-only score edit (PLAN §8.7). Represents accepting the AI draft or an override.</summary>
public sealed record BoardScoreEdit(int Score, string? Summary, string ExpectedRowVersion);

/// <summary>
/// Board actions on a stored attempt: review, correct the score, retry grading. Every mutation
/// writes the audit log and keeps the task's point entry explainable.
/// </summary>
public interface IBoardSubmissionService
{
    Task<BoardSubmissionDetail?> FindAsync(Guid submissionId, CancellationToken cancellationToken = default);

    Task<Result> SaveScoreAsync(
        Guid submissionId,
        BoardScoreEdit edit,
        Guid boardUserId,
        CancellationToken cancellationToken = default);

    Task<Result> RetryAsync(Guid submissionId, Guid boardUserId, CancellationToken cancellationToken = default);
}

public sealed class BoardSubmissionService(
    AppDbContext db,
    ICSharpCreditService credit,
    IGradingQueue gradingQueue,
    IOptions<AiOptions> ai,
    TimeProvider timeProvider,
    ILogger<BoardSubmissionService> logger) : IBoardSubmissionService
{
    public async Task<BoardSubmissionDetail?> FindAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.Id == submissionId)
            .Select(submission => new
            {
                submission.Id,
                submission.TaskId,
                submission.UserId,
                TaskTitle = submission.Task!.Title,
                submission.Task.DescriptionMd,
                submission.Task.ReferenceFolderPath,
                submission.Task.ReferenceFileCount,
                Year = submission.Task.MonthSheet!.Year,
                Month = submission.Task.MonthSheet.Month,
                MemberName = submission.User!.DisplayName,
                MemberEmail = submission.User.Email,
                submission.AttemptNo,
                submission.SubmittedAtUtc,
                submission.IsLate,
                submission.FileCount,
                submission.IgnoredFileCount,
                submission.TotalBytes,
                submission.BundleChars,
                submission.Status,
                submission.Score,
                submission.AiScore,
                submission.IsManualScore,
                submission.Summary,
                submission.Verdict,
                submission.ErrorMessage,
                submission.GradedAtUtc,
                submission.GradingModel,
                submission.PromptVersion,
                submission.PromptHash,
                submission.InputChars,
                submission.InputTokens,
                submission.OutputTokens,
                submission.RetryCount,
                submission.FeedbackJson,
                submission.RowVersion
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new BoardSubmissionDetail(
            row.Id,
            row.TaskId,
            row.UserId,
            row.TaskTitle,
            MonthLabels.Format(row.Year, row.Month),
            row.MemberName,
            row.MemberEmail ?? string.Empty,
            row.AttemptNo,
            row.SubmittedAtUtc,
            row.IsLate,
            row.FileCount,
            row.IgnoredFileCount,
            row.TotalBytes,
            row.BundleChars,
            row.Status,
            row.Score,
            row.AiScore,
            row.IsManualScore,
            row.Summary,
            row.Verdict,
            row.ErrorMessage,
            row.GradedAtUtc,
            row.GradingModel,
            row.PromptVersion,
            row.PromptHash,
            row.InputChars,
            row.InputTokens,
            row.OutputTokens,
            row.RetryCount,
            GradingFeedbackJson.Deserialize(row.FeedbackJson),
            Convert.ToBase64String(row.RowVersion),
            row.DescriptionMd,
            row.ReferenceFolderPath,
            row.ReferenceFileCount,
            ai.Value.IsConfigured);
    }

    public async Task<Result> SaveScoreAsync(
        Guid submissionId,
        BoardScoreEdit edit,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        if (edit.Score is < SubmissionScoring.MinimumScore or > SubmissionScoring.MaxPoints)
        {
            return Result.Fail(
                "invalid_score",
                $"Enter a whole number from {SubmissionScoring.MinimumScore} to {SubmissionScoring.MaxPoints}.");
        }

        if (!TryDecodeRowVersion(edit.ExpectedRowVersion, out var expected))
        {
            return Result.Fail("stale_review", "This page is out of date. Reload the attempt and try again.");
        }

        var submission = await db.CSharpSubmissions
            .FirstOrDefaultAsync(row => row.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            return Result.Fail("not_found", "That attempt no longer exists.");
        }

        if (!CryptographicOperations.FixedTimeEquals(submission.RowVersion, expected))
        {
            return Result.Fail(
                "conflict",
                "Someone else edited this attempt while you had it open. Reload the page and try again.");
        }

        var now = timeProvider.GetUtcNow();
        submission.Score = edit.Score;
        submission.IsManualScore = true;

        // The board may rewrite the AI's wording as well as its score.
        var summary = string.IsNullOrWhiteSpace(edit.Summary) ? null : edit.Summary.Trim();
        submission.Summary = summary;
        UpdateFeedbackSummary(submission, summary);

        // A score typed by the board is a finished grade, even when AI grading never ran.
        if (submission.Status != SubmissionStatus.Graded)
        {
            submission.Status = SubmissionStatus.Graded;
            submission.GradedAtUtc = now;
            submission.ErrorMessage = null;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception, "Concurrent score edit on submission {SubmissionId}.", submissionId);
            return Result.Fail(
                "conflict",
                "Someone else edited this attempt while you had it open. Reload the page and try again.");
        }

        await credit.ApplyBoardEditAsync(submission.TaskId, submission.UserId, submission.Id, cancellationToken);

        return Result.Ok(submission.IsLate
            ? "Score saved. It counts despite the late flag, as you confirmed."
            : "Score saved and the point entry updated.");
    }

    public async Task<Result> RetryAsync(
        Guid submissionId,
        Guid boardUserId,
        CancellationToken cancellationToken = default)
    {
        var submission = await db.CSharpSubmissions
            .FirstOrDefaultAsync(row => row.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            return Result.Fail("not_found", "That attempt no longer exists.");
        }

        if (submission.Status == SubmissionStatus.Grading)
        {
            return Result.Fail("already_grading", "That attempt is already being graded.");
        }

        submission.Status = SubmissionStatus.Uploaded;
        submission.ErrorMessage = null;
        submission.RetryCount = 0;
        submission.GradedAtUtc = null;

        await db.SaveChangesAsync(cancellationToken);

        // Force skips identical-bundle reuse: the board asked for a fresh look.
        gradingQueue.Enqueue(submission.Id, force: true);

        return Result.Ok("Grading queued again. The member sees the result when it finishes.");
    }

    private static void UpdateFeedbackSummary(CSharpSubmission submission, string? summary)
    {
        if (string.IsNullOrWhiteSpace(submission.FeedbackJson) || summary is null)
        {
            return;
        }

        var feedback = GradingFeedbackJson.Deserialize(submission.FeedbackJson);
        if (feedback is null || string.Equals(feedback.Summary, summary, StringComparison.Ordinal))
        {
            return;
        }

        submission.FeedbackJson = GradingFeedbackJson.Serialize(feedback with { Summary = summary });
    }

    private static bool TryDecodeRowVersion(string? raw, out byte[] rowVersion)
    {
        rowVersion = [];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(raw);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}