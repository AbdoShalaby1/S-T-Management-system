using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;

namespace OscCommittee.Web.Services.Points;

/// <summary>One ledger row, with the source title and actor already resolved.</summary>
public sealed record MemberPointEntry(
    Guid Id,
    PointCategory Category,
    string? SourceLabel,
    string? SourceDetail,
    int BasePoints,
    int FinalPoints,
    bool IsManualOverride,
    string? Reason,
    DateTimeOffset CreatedAtUtc,
    string? ActorName)
{
    public bool IsManual => IsManualOverride || Category == PointCategory.Adjustment;
}

/// <summary>One month's slice of a member's ledger, plus the category totals the board reads.</summary>
public sealed record MemberMonthPoints(
    Guid MonthSheetId,
    int Year,
    int Month,
    string Label,
    MonthStatus Status,
    int DsPoints,
    int TaskPoints,
    int BonusPoints,
    int AdjustmentPoints,
    int Total,
    IReadOnlyList<MemberPointEntry> Entries);

/// <summary>
/// Reads and writes <see cref="PointEntry"/> rows. Totals are always summed on demand
/// (PLAN §6.5); there is no denormalised total anywhere.
/// </summary>
public interface IPointsService
{
    /// <summary>One member's total for one month.</summary>
    Task<int> GetTotalAsync(Guid userId, Guid monthSheetId, CancellationToken cancellationToken = default);

    /// <summary>One member's total for one month, split by category.</summary>
    Task<IReadOnlyDictionary<PointCategory, int>> GetCategoryTotalsAsync(
        Guid userId,
        Guid monthSheetId,
        CancellationToken cancellationToken = default);

    /// <summary>Every member's total for one month, for the member list.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetMonthTotalsAsync(
        Guid monthSheetId,
        CancellationToken cancellationToken = default);

    /// <summary>Every member's total across all months.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetAllTimeTotalsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The full month-by-month breakdown, newest month first.</summary>
    Task<IReadOnlyList<MemberMonthPoints>> GetMemberMonthsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result> AddBonusAsync(
        Guid userId,
        Guid monthSheetId,
        int amount,
        string? reason,
        Guid boardUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pure amount rules shared by the board form and the service, so a bonus or adjustment is
/// validated identically wherever it enters the ledger.
/// </summary>
public static class PointEntryRules
{
    /// <summary>PLAN §6.8 allows any integer; this is a sanity bound, not a rule from the plan.</summary>
    public const int MaxAmount = 999;

    public const int MinAmount = -999;

    /// <summary>Null when the amount is acceptable, otherwise a code and reason for the board.</summary>
    public static (string Code, string Message)? Validate(int amount) =>
        amount is < MinAmount or > MaxAmount
            ? ("invalid_amount", $"Enter an amount between {MinAmount} and {MaxAmount}.")
            : amount == 0
                ? ("zero_amount", "Enter a non-zero amount.")
                : null;

    /// <summary>Always signed: <c>+5</c>, <c>-3</c>, <c>0</c>.</summary>
    public static string FormatSigned(int amount) =>
        amount.ToString("+#;-#;0", CultureInfo.InvariantCulture);
}

public sealed class PointsService(
    AppDbContext db,
    ITimeZoneService timeZone,
    TimeProvider timeProvider,
    ILogger<PointsService> logger) : IPointsService
{
    public async Task<int> GetTotalAsync(
        Guid userId,
        Guid monthSheetId,
        CancellationToken cancellationToken = default) =>
        await db.PointEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.MonthSheetId == monthSheetId)
            .SumAsync(entry => (int?)entry.FinalPoints, cancellationToken) ?? 0;

    public async Task<IReadOnlyDictionary<PointCategory, int>> GetCategoryTotalsAsync(
        Guid userId,
        Guid monthSheetId,
        CancellationToken cancellationToken = default)
    {
        var totals = await db.PointEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.MonthSheetId == monthSheetId)
            .GroupBy(entry => entry.Category)
            .Select(group => new { Category = group.Key, Points = group.Sum(entry => entry.FinalPoints) })
            .ToListAsync(cancellationToken);

        return totals.ToDictionary(row => row.Category, row => row.Points);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetMonthTotalsAsync(
        Guid monthSheetId,
        CancellationToken cancellationToken = default)
    {
        var totals = await db.PointEntries
            .AsNoTracking()
            .Where(entry => entry.MonthSheetId == monthSheetId)
            .GroupBy(entry => entry.UserId)
            .Select(group => new { UserId = group.Key, Points = group.Sum(entry => entry.FinalPoints) })
            .ToListAsync(cancellationToken);

        return totals.ToDictionary(row => row.UserId, row => row.Points);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetAllTimeTotalsAsync(
        CancellationToken cancellationToken = default)
    {
        var totals = await db.PointEntries
            .AsNoTracking()
            .GroupBy(entry => entry.UserId)
            .Select(group => new { UserId = group.Key, Points = group.Sum(entry => entry.FinalPoints) })
            .ToListAsync(cancellationToken);

        return totals.ToDictionary(row => row.UserId, row => row.Points);
    }

    public async Task<IReadOnlyList<MemberMonthPoints>> GetMemberMonthsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.PointEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.MonthSheet!.Year)
            .ThenByDescending(entry => entry.MonthSheet!.Month)
            .ThenBy(entry => entry.Category)
            .ThenBy(entry => entry.CreatedAtUtc)
            .Select(entry => new LedgerRow(
                entry.Id,
                entry.MonthSheetId,
                entry.MonthSheet!.Year,
                entry.MonthSheet.Month,
                entry.MonthSheet.Status,
                entry.Category,
                entry.SourceId,
                entry.BasePoints,
                entry.FinalPoints,
                entry.IsManualOverride,
                entry.Reason,
                entry.CreatedAtUtc,
                entry.CreatedByUserId))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var solveIds = rows
            .Where(row => row.Category == PointCategory.DsSolve && row.SourceId is not null)
            .Select(row => row.SourceId!.Value)
            .Distinct()
            .ToList();

        var submissionIds = rows
            .Where(row => row.Category == PointCategory.CSharpSubmission && row.SourceId is not null)
            .Select(row => row.SourceId!.Value)
            .Distinct()
            .ToList();

        var solveTitles = solveIds.Count == 0
            ? new Dictionary<Guid, (string Title, string Sheet)>()
            : (await db.DsSolves
                .AsNoTracking()
                .Where(solve => solveIds.Contains(solve.Id))
                .Select(solve => new
                {
                    solve.Id,
                    Title = solve.DsProblem!.Title,
                    Sheet = solve.DsProblem.DsSheet!.Title
                })
                .ToListAsync(cancellationToken))
                .ToDictionary(row => row.Id, row => (row.Title, row.Sheet));

        var taskTitles = submissionIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.CSharpSubmissions
                .AsNoTracking()
                .Where(submission => submissionIds.Contains(submission.Id))
                .Select(submission => new { submission.Id, Title = submission.Task!.Title })
                .ToDictionaryAsync(row => row.Id, row => row.Title, cancellationToken);

        var actorIds = rows
            .Where(row => row.CreatedByUserId is not null)
            .Select(row => row.CreatedByUserId!.Value)
            .Distinct()
            .ToList();

        var actors = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users
                .AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .Select(user => new { user.Id, user.DisplayName })
                .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);

        return rows
            .GroupBy(row => new
            {
                row.MonthSheetId,
                row.Year,
                row.Month,
                row.Status
            })
            .Select(group => new MemberMonthPoints(
                group.Key.MonthSheetId,
                group.Key.Year,
                group.Key.Month,
                MonthLabels.Format(group.Key.Year, group.Key.Month),
                group.Key.Status,
                PointsFor(group, PointCategory.DsSolve),
                PointsFor(group, PointCategory.CSharpSubmission),
                PointsFor(group, PointCategory.Bonus),
                PointsFor(group, PointCategory.Adjustment),
                group.Sum(row => row.FinalPoints),
                group
                    .Select(row => new MemberPointEntry(
                        row.Id,
                        row.Category,
                        SourceLabel(row.Category, row.SourceId, solveTitles, taskTitles),
                        SourceDetail(row.Category, row.SourceId, solveTitles),
                        row.BasePoints,
                        row.FinalPoints,
                        row.IsManualOverride,
                        row.Reason,
                        row.CreatedAtUtc,
                        row.CreatedByUserId is { } actorId ? actors.GetValueOrDefault(actorId) : null))
                    .ToList()))
            .ToList();
    }

    public Task<Result> AddBonusAsync(
        Guid userId,
        Guid monthSheetId,
        int amount,
        string? reason,
        Guid boardUserId,
        CancellationToken cancellationToken = default) =>
        AddBoardEntryAsync(
            PointCategory.Bonus,
            userId,
            monthSheetId,
            amount,
            reason,
            boardUserId,
            cancellationToken);

    private async Task<Result> AddBoardEntryAsync(
        PointCategory category,
        Guid userId,
        Guid monthSheetId,
        int amount,
        string? reason,
        Guid boardUserId,
        CancellationToken cancellationToken)
    {
        var allowed = "Bonus";

        if (PointEntryRules.Validate(amount) is { } problem)
        {
            return Result.Fail(problem.Code, problem.Message);
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is { Length: > 400 })
        {
            return Result.Fail("reason_too_long", "Keep the reason under 400 characters.");
        }

        var memberExists = await db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);

        if (!memberExists)
        {
            return Result.Fail("member_missing", "That member no longer exists.");
        }

        var month = await db.MonthSheets
            .AsNoTracking()
            .Where(sheet => sheet.Id == monthSheetId)
            .Select(sheet => new { sheet.Id, sheet.Year, sheet.Month })
            .FirstOrDefaultAsync(cancellationToken);

        if (month is null)
        {
            return Result.Fail("month_missing", "That month no longer exists.");
        }

        var (currentYear, currentMonth) = CurrentLocalMonth();
        var (previousYear, previousMonth) = PreviousMonth(currentYear, currentMonth);

        var isAllowedMonth =
            (month.Year == currentYear && month.Month == currentMonth)
            || (month.Year == previousYear && month.Month == previousMonth);

        if (!isAllowedMonth)
        {
            return Result.Fail(
                "month_not_allowed",
                "Bonuses can only target the current or previous month.");
        }

        var now = timeProvider.GetUtcNow();
        db.PointEntries.Add(new PointEntry
        {
            UserId = userId,
            MonthSheetId = month.Id,
            Category = category,
            SourceId = null,
            BasePoints = amount,
            FinalPoints = amount,
            IsManualOverride = false,
            Reason = trimmedReason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = boardUserId
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "{Category} of {Amount} points for {UserId} in {Year}-{Month} by {BoardUserId}.",
            category,
            amount,
            userId,
            month.Year,
            month.Month,
            boardUserId);

        return Result.Ok(
            $"{allowed} of {PointEntryRules.FormatSigned(amount)} points saved to {MonthLabels.Format(month.Year, month.Month)}.");
    }

    private (int Year, int Month) CurrentLocalMonth()
    {
        var now = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone.TimeZone);
        return (now.Year, now.Month);
    }

    private static (int Year, int Month) PreviousMonth(int year, int month) =>
        month == 1 ? (year - 1, 12) : (year, month - 1);

    private static int PointsFor(
        IEnumerable<LedgerRow> group,
        PointCategory category) =>
        group
            .Where(row => row.Category == category)
            .Sum(row => row.FinalPoints);

    private static string? SourceLabel(
        PointCategory category,
        Guid? sourceId,
        IReadOnlyDictionary<Guid, (string Title, string Sheet)> solveTitles,
        IReadOnlyDictionary<Guid, string> taskTitles) =>
        category switch
        {
            PointCategory.DsSolve when sourceId is { } solveId => solveTitles.GetValueOrDefault(solveId).Title,
            PointCategory.CSharpSubmission when sourceId is { } submissionId => taskTitles.GetValueOrDefault(submissionId),
            _ => null
        };

    /// <summary>The sheet a DS solve came from, so the board sees where the points were earned.</summary>
    private static string? SourceDetail(
        PointCategory category,
        Guid? sourceId,
        IReadOnlyDictionary<Guid, (string Title, string Sheet)> solveTitles) =>
        category == PointCategory.DsSolve && sourceId is { } solveId
            ? solveTitles.GetValueOrDefault(solveId).Sheet
            : null;

    /// <summary>Flat projection so grouping stays strongly typed outside the query.</summary>
    private sealed record LedgerRow(
        Guid Id,
        Guid MonthSheetId,
        int Year,
        int Month,
        MonthStatus Status,
        PointCategory Category,
        Guid? SourceId,
        int BasePoints,
        int FinalPoints,
        bool IsManualOverride,
        string? Reason,
        DateTimeOffset CreatedAtUtc,
        Guid? CreatedByUserId);
}