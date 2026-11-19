using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;

namespace OscCommittee.Web.Services.Ds;

/// <summary>What to credit. The platform id is taken from the problem, never from the caller.</summary>
public sealed record DsCreditRequest(
    Guid UserId,
    Guid ProblemId,
    string SubmissionId,
    DateTimeOffset SolvedAtUtc,
    string? Language,
    string PlatformUrl,
    bool IsManual = false,
    Guid? CreditedByUserId = null,
    string? Reason = null);

public sealed record DsCreditResult(
    bool Credited,
    bool Duplicate,
    string? ErrorCode,
    string Message,
    Guid? SolveId,
    Guid ProblemId,
    string ProblemTitle,
    int Points,
    bool IsLate,
    int Year,
    int Month)
{
    public static DsCreditResult Fail(string errorCode, string message) =>
        new(false, false, errorCode, message, null, Guid.Empty, string.Empty, 0, false, 0, 0);

    public static DsCreditResult AlreadyCredited(Guid solveId, Guid problemId, string title) =>
        new(false, true, null, "Already credited.", solveId, problemId, title, 0, false, 0, 0);
}

/// <summary>
/// Creates the solve and its point entry in one save (one implicit transaction), idempotent through
/// the unique <c>(UserId, DsProblemId)</c> index. The same platform problem may be credited on
/// several sheets, because each sheet carries its own copy. Shared by the sync engine and the
/// board's manual credit so both follow the same cutoff, late and month rules.
/// </summary>
public interface IDsCreditService
{
    Task<DsCreditResult> CreditAsync(DsCreditRequest request, CancellationToken cancellationToken = default);
}

public sealed class DsCreditService(
    AppDbContext db,
    IMonthSheetService months,
    ITimeZoneService timeZone,
    TimeProvider timeProvider,
    ILogger<DsCreditService> logger) : IDsCreditService
{
    public async Task<DsCreditResult> CreditAsync(
        DsCreditRequest request,
        CancellationToken cancellationToken = default)
    {
        var problem = await db.DsProblems
            .AsNoTracking()
            .Where(row => row.Id == request.ProblemId)
            .Select(row => new
            {
                row.Id,
                row.Platform,
                row.ExternalId,
                row.Title,
                row.Points,
                row.AssignedAtUtc,
                DueAtUtc = row.DsSheet!.DueAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (problem is null)
        {
            return DsCreditResult.Fail("not_found", "That problem no longer exists.");
        }

        var externalId = DsSyncRules.NormalizeExternalId(problem.Platform, problem.ExternalId);

        var existing = await db.DsSolves
            .AsNoTracking()
            .Where(solve => solve.UserId == request.UserId
                && solve.DsProblemId == problem.Id)
            .Select(solve => (Guid?)solve.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is { } solveId)
        {
            return DsCreditResult.AlreadyCredited(solveId, problem.Id, problem.Title);
        }

        if (!DsSyncRules.IsAfterAssignment(problem.AssignedAtUtc, request.SolvedAtUtc))
        {
            return DsCreditResult.Fail(
                "before_assignment",
                $"That submission is older than “{problem.Title}”'s assignment, so it cannot credit points.");
        }

        var isLate = DsSyncRules.IsLate(request.SolvedAtUtc, problem.DueAtUtc);
        var points = isLate ? 0 : problem.Points;
        var (year, month) = DsSyncRules.CompletionMonth(request.SolvedAtUtc, timeZone.TimeZone);
        var monthSheet = await months.GetOrCreateAsync(year, month, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var solve = new DsSolve
        {
            UserId = request.UserId,
            DsProblemId = problem.Id,
            Platform = problem.Platform,
            ExternalId = externalId,
            PlatformSubmissionId = request.SubmissionId,
            SolvedAtUtc = request.SolvedAtUtc,
            Language = request.Language,
            PlatformUrl = request.PlatformUrl,
            IsLate = isLate,
            IsManual = request.IsManual,
            CreatedAtUtc = now
        };

        var entry = new PointEntry
        {
            UserId = request.UserId,
            MonthSheetId = monthSheet.Id,
            Category = PointCategory.DsSolve,
            SourceId = solve.Id,
            BasePoints = points,
            FinalPoints = points,
            IsManualOverride = false,
            Reason = isLate ? "Late solve — worth 0." : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = request.IsManual ? request.CreditedByUserId : null
        };

        db.DsSolves.Add(solve);
        db.PointEntries.Add(entry);

        try
        {
            // One SaveChanges = one transaction for both rows.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(
                ex,
                "Crediting {ExternalId} for {UserId} hit a uniqueness rule.",
                externalId,
                request.UserId);

            db.Entry(solve).State = EntityState.Detached;
            db.Entry(entry).State = EntityState.Detached;

            var raced = await db.DsSolves
                .AsNoTracking()
                .Where(row => row.UserId == request.UserId
                    && row.DsProblemId == problem.Id)
                .Select(row => (Guid?)row.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return raced is { } racedId
                ? DsCreditResult.AlreadyCredited(racedId, problem.Id, problem.Title)
                : DsCreditResult.Fail("credit_failed", "That solve could not be saved. Try again.");
        }

        if (request.IsManual)
        {
            logger.LogInformation(
                "Solve {ExternalId} for {UserId} was credited manually by {BoardUserId}.",
                externalId,
                request.UserId,
                request.CreditedByUserId);
        }

        return new DsCreditResult(
            true,
            false,
            null,
            isLate
                ? $"“{problem.Title}” was late, so it is recorded at 0 points."
                : $"“{problem.Title}” credited {points} points.",
            solve.Id,
            problem.Id,
            problem.Title,
            points,
            isLate,
            year,
            month);
    }
}