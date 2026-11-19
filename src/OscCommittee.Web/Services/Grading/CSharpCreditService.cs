using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Tasks;

namespace OscCommittee.Web.Services.Grading;

/// <summary>A graded attempt reduced to the facts the credit rule needs (PLAN §6.7).</summary>
public sealed record CreditableAttempt(
    Guid Id,
    int AttemptNo,
    int? Score,
    bool IsLate,
    bool IsManualScore,
    DateTimeOffset SubmittedAtUtc);

/// <summary>
/// PLAN §6.7 selection: highest effective points, tie-broken by the earliest submission.
/// Late is worth 0, so a late 9 never beats an on-time 6. A board-edited score is authoritative
/// and counts even on a late attempt, which is why <see cref="CreditableAttempt.IsManualScore"/>
/// exists.
/// </summary>
public static class CSharpCreditRules
{
    public static CreditableAttempt? Select(IEnumerable<CreditableAttempt> attempts) =>
        attempts
            .Where(attempt => attempt.Score.HasValue)
            .Select(attempt => new
            {
                Attempt = attempt,
                Points = EffectivePoints(attempt)
            })
            .OrderByDescending(candidate => candidate.Points)
            .ThenBy(candidate => candidate.Attempt.SubmittedAtUtc)
            .ThenBy(candidate => candidate.Attempt.AttemptNo)
            .Select(candidate => candidate.Attempt)
            .FirstOrDefault();

    /// <summary>A manual score counts as-is; an automatic late score is still worth 0.</summary>
    public static int EffectivePoints(CreditableAttempt attempt)
    {
        if (attempt.Score is null)
        {
            return 0;
        }

        return attempt.IsManualScore
            ? Math.Clamp(attempt.Score.Value, 1, SubmissionScoring.MaxPoints)
            : SubmissionScoring.Points(attempt.Score, attempt.IsLate)!.Value;
    }
}

public interface ICSharpCreditService
{
    /// <summary>
    /// Automatic path: rebuilds the single <see cref="PointCategory.CSharpSubmission"/> entry for
    /// this member and task so it mirrors the creditable attempt. Manual overrides are left alone.
    /// </summary>
    Task<Result> ApplyAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The board saved a score. When that attempt is now the creditable one the entry follows it and
    /// is marked manual; otherwise normal rules apply.
    /// </summary>
    Task<Result> ApplyBoardEditAsync(
        Guid taskId,
        Guid userId,
        Guid submissionId,
        CancellationToken cancellationToken = default);
}

public sealed class CSharpCreditService(
    AppDbContext db,
    IMonthSheetService months,
    ITimeZoneService timeZone,
    TimeProvider timeProvider,
    ILogger<CSharpCreditService> logger) : ICSharpCreditService
{
    private const string BoardEditReason = "Board-adjusted score.";
    private const string BoardLateReason = "Board override on a late attempt.";
    private const string LateReason = "Late submission - worth 0.";

    public async Task<Result> ApplyAsync(
        Guid taskId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var attempts = await LoadAttemptsAsync(taskId, userId, cancellationToken);
        var best = CSharpCreditRules.Select(attempts);
        if (best is null)
        {
            return Result.Ok("No graded attempt to credit yet.");
        }

        var entry = await FindEntryAsync(
            userId,
            attempts.Select(attempt => attempt.Id),
            cancellationToken);
        var monthSheetId = await MonthSheetIdAsync(best, cancellationToken);

        if (entry is null)
        {
            AddEntry(best, monthSheetId, isManual: false, userId);
            await db.SaveChangesAsync(cancellationToken);
            LogCredit(taskId, userId, best, "Credited");
            return Result.Ok("Credit updated.");
        }

        // PLAN §6.7: a board override sticks until the board changes it again.
        if (entry.IsManualOverride)
        {
            return Result.Ok("Manual override left alone.");
        }

        UpdateEntry(entry, best, monthSheetId, isManual: false);
        await db.SaveChangesAsync(cancellationToken);
        LogCredit(taskId, userId, best, "Updated");
        return Result.Ok("Credit updated.");
    }

    public async Task<Result> ApplyBoardEditAsync(
        Guid taskId,
        Guid userId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var attempts = await LoadAttemptsAsync(taskId, userId, cancellationToken);
        var best = CSharpCreditRules.Select(attempts);
        if (best is null)
        {
            return Result.Ok("No graded attempt to credit yet.");
        }

        var entry = await FindEntryAsync(
            userId,
            attempts.Select(attempt => attempt.Id),
            cancellationToken);
        var monthSheetId = await MonthSheetIdAsync(best, cancellationToken);

        if (best.Id != submissionId)
        {
            // The edited attempt is not the best one; normal rules still decide the ledger.
            if (entry is null || !entry.IsManualOverride)
            {
                if (entry is null)
                {
                    AddEntry(best, monthSheetId, isManual: false, userId);
                }
                else
                {
                    UpdateEntry(entry, best, monthSheetId, isManual: false);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            return Result.Ok("Score saved. The creditable attempt did not change.");
        }

        if (entry is null)
        {
            AddEntry(best, monthSheetId, isManual: true, userId);
        }
        else
        {
            UpdateEntry(entry, best, monthSheetId, isManual: true);
        }

        await db.SaveChangesAsync(cancellationToken);
        LogCredit(taskId, userId, best, "Board override");
        return Result.Ok("Score saved and the ledger updated.");
    }

    private Task<List<CreditableAttempt>> LoadAttemptsAsync(
        Guid taskId,
        Guid userId,
        CancellationToken cancellationToken) =>
        db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.TaskId == taskId && submission.UserId == userId)
            .Select(submission => new CreditableAttempt(
                submission.Id,
                submission.AttemptNo,
                submission.Score,
                submission.IsLate,
                submission.IsManualScore,
                submission.SubmittedAtUtc))
            .ToListAsync(cancellationToken);

    private Task<PointEntry?> FindEntryAsync(
        Guid userId,
        IEnumerable<Guid> sourceIds,
        CancellationToken cancellationToken)
    {
        var ids = sourceIds.Distinct().ToList();

        return db.PointEntries
            .Where(point => point.UserId == userId
                && point.Category == PointCategory.CSharpSubmission
                && point.SourceId != null
                && ids.Contains(point.SourceId.Value))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private void AddEntry(
        CreditableAttempt best,
        Guid monthSheetId,
        bool isManual,
        Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var points = CSharpCreditRules.EffectivePoints(best);

        db.PointEntries.Add(new PointEntry
        {
            UserId = userId,
            MonthSheetId = monthSheetId,
            Category = PointCategory.CSharpSubmission,
            SourceId = best.Id,
            BasePoints = points,
            FinalPoints = points,
            IsManualOverride = isManual,
            Reason = ReasonFor(best, isManual),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private void UpdateEntry(
        PointEntry entry,
        CreditableAttempt best,
        Guid monthSheetId,
        bool isManual)
    {
        var points = CSharpCreditRules.EffectivePoints(best);
        entry.SourceId = best.Id;
        entry.MonthSheetId = monthSheetId;
        entry.BasePoints = points;
        entry.FinalPoints = points;
        entry.IsManualOverride = isManual;
        entry.Reason = ReasonFor(best, isManual);
        entry.UpdatedAtUtc = timeProvider.GetUtcNow();
    }

    private async Task<Guid> MonthSheetIdAsync(
        CreditableAttempt best,
        CancellationToken cancellationToken = default)
    {
        var month = TimeZoneInfo.ConvertTime(best.SubmittedAtUtc, timeZone.TimeZone);
        var sheet = await months.GetOrCreateAsync(month.Year, month.Month, cancellationToken);
        return sheet.Id;
    }

    private static string? ReasonFor(CreditableAttempt best, bool isManual) =>
        best.IsLate
            ? isManual ? BoardLateReason : LateReason
            : isManual ? BoardEditReason : null;

    private void LogCredit(Guid taskId, Guid userId, CreditableAttempt best, string action) =>
        logger.LogInformation(
            "{Action} C# task {TaskId} for {UserId}: attempt {AttemptNo}, {Points} points.",
            action,
            taskId,
            userId,
            best.AttemptNo,
            CSharpCreditRules.EffectivePoints(best));
}