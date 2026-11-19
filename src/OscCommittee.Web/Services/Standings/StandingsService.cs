using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Tasks;

namespace OscCommittee.Web.Services.Standings;

/// <summary>The ranked table plus the cohort size the rank chip reads.</summary>
public sealed record StandingsSummaryData(
    IReadOnlyList<RankedStanding> Rows,
    int ParticipantCount);

/// <summary>A member the detail selector may open.</summary>
public sealed record StandingsMemberOption(Guid Id, string DisplayName, UserStatus Status);

/// <summary>The header of a detailed standing.</summary>
public sealed record StandingsMemberInfo(
    Guid Id,
    string DisplayName,
    UserStatus Status,
    DateTimeOffset RegisteredAtUtc);

/// <summary>One month in the detailed table.</summary>
public sealed record StandingsMonthRow(
    Guid MonthSheetId,
    int Year,
    int Month,
    string Label,
    MonthStatus Status,
    StandingsTally Totals);

/// <summary>Counts only — the problem list never leaks through standings (PLAN §9).</summary>
public sealed record StandingsSheetRow(
    Guid SheetId,
    string Title,
    int Solved,
    int Total,
    int Points);

/// <summary>
/// One task line. <paramref name="SubmissionId"/> is only rendered as a link for the board;
/// members see the score and the late flag.
/// </summary>
public sealed record StandingsTaskRow(
    Guid TaskId,
    string Title,
    Guid? SubmissionId,
    int AttemptCount,
    int? Score,
    int Points,
    bool IsLate,
    SubmissionStatus? Status);

/// <summary>A bonus or adjustment with its reason.</summary>
public sealed record StandingsBonusRow(
    PointCategory Category,
    string? Reason,
    int Points,
    DateTimeOffset CreatedAtUtc);

/// <summary>Everything under the month table for the selected month.</summary>
public sealed record StandingsMonthDetail(
    StandingsMonthRow Row,
    IReadOnlyList<StandingsSheetRow> Sheets,
    IReadOnlyList<StandingsTaskRow> Tasks,
    IReadOnlyList<StandingsBonusRow> Bonuses);

public sealed record StandingsMemberDetail(
    StandingsMemberInfo Member,
    IReadOnlyList<StandingsMonthRow> Months,
    StandingsMonthDetail? SelectedMonth);

/// <summary>Active DS sheet facts shown in the context strip and on the dashboard.</summary>
public sealed record StandingsDsContext(
    Guid SheetId,
    string Title,
    string MonthLabel,
    DateTimeOffset DueAtUtc,
    int Solved,
    int Total,
    int Points);

/// <summary>Active C# task facts for the viewer.</summary>
public sealed record StandingsTaskContext(
    Guid TaskId,
    string Title,
    DateTimeOffset DueAtUtc,
    string StatusLabel,
    int? Score,
    int Attempts,
    bool IsLate);

public sealed record StandingsContext(
    StandingsDsContext? DsSheet,
    StandingsTaskContext? Task);

/// <summary>One "needs attention" banner on the dashboard.</summary>
public sealed record DashboardAlert(
    string Kind,
    string Title,
    string Message,
    string? Href,
    string? ActionLabel);

/// <summary>Everything the upgraded dashboard needs beyond the DS sync button.</summary>
public sealed record DashboardSnapshot(
    string MonthLabel,
    int MonthTotal,
    int? Rank,
    int CohortSize,
    StandingsDsContext? ActiveDsSheet,
    StandingsTaskContext? ActiveTask,
    IReadOnlyList<DashboardAlert> Alerts);

/// <summary>
/// Standings and dashboard reads (PLAN §7.1 and §9). Every total is grouped from
/// <c>PointEntry.FinalPoints</c> on request; nothing is cached.
/// </summary>
public interface IStandingsService
{
    /// <summary>The current local month when it is open, otherwise the newest open month.</summary>
    Task<MonthSheetSummary?> GetOpenMonthAsync(CancellationToken cancellationToken = default);

    /// <summary>Every month, newest first, for the detail selector.</summary>
    Task<IReadOnlyList<MonthSheetSummary>> GetMonthsAsync(CancellationToken cancellationToken = default);

    Task<StandingsSummaryData> GetSummaryAsync(
        Guid monthSheetId,
        StandingsTab tab,
        bool includeDeactivated,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StandingsMemberOption>> GetMemberOptionsAsync(
        bool includeDeactivated,
        CancellationToken cancellationToken = default);

    Task<StandingsMemberDetail?> GetMemberDetailAsync(
        Guid memberId,
        Guid? monthSheetId,
        bool includeDeactivated,
        bool includeHidden,
        CancellationToken cancellationToken = default);

    /// <summary>Active DS sheet progress and active task status for one viewer.</summary>
    Task<StandingsContext> GetContextAsync(Guid viewerId, CancellationToken cancellationToken = default);

    Task<DashboardSnapshot> GetDashboardAsync(Guid viewerId, CancellationToken cancellationToken = default);
}

public sealed class StandingsService(
    AppDbContext db,
    IMonthSheetService months,
    ITimeZoneService timeZone) : IStandingsService
{
    public async Task<MonthSheetSummary?> GetOpenMonthAsync(CancellationToken cancellationToken = default)
    {
        var local = timeZone.NowLocal();
        var all = await months.GetListAsync(cancellationToken);

        return all.FirstOrDefault(month => month.IsOpen && month.Year == local.Year && month.Month == local.Month)
            ?? all.FirstOrDefault(month => month.IsOpen);
    }

    public Task<IReadOnlyList<MonthSheetSummary>> GetMonthsAsync(CancellationToken cancellationToken = default) =>
        months.GetListAsync(cancellationToken);

    public async Task<StandingsSummaryData> GetSummaryAsync(
        Guid monthSheetId,
        StandingsTab tab,
        bool includeDeactivated,
        CancellationToken cancellationToken = default)
    {
        var users = await EligibleUsersAsync(includeDeactivated, cancellationToken);

        var entries = await db.PointEntries
            .AsNoTracking()
            .Where(entry => entry.MonthSheetId == monthSheetId)
            .Select(entry => new { entry.UserId, entry.Category, entry.FinalPoints })
            .ToListAsync(cancellationToken);

        var tallies = entries
            .GroupBy(entry => entry.UserId)
            .ToDictionary(
                group => group.Key,
                group => StandingsRules.Tally(
                    group.Select(entry => new StandingPoint(entry.Category, entry.FinalPoints))));

        var candidates = users.Select(user => new StandingsCandidate(
            user.Id,
            user.DisplayName,
            tallies.GetValueOrDefault(user.Id) ?? StandingsTally.Empty,
            user.CreatedAtUtc));

        return new StandingsSummaryData(
            StandingsRules.Rank(candidates, tab),
            users.Count);
    }

    public async Task<IReadOnlyList<StandingsMemberOption>> GetMemberOptionsAsync(
        bool includeDeactivated,
        CancellationToken cancellationToken = default)
    {
        var users = await EligibleUsersAsync(includeDeactivated, cancellationToken);

        return users
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => new StandingsMemberOption(user.Id, user.DisplayName, user.Status))
            .ToList();
    }

    public async Task<StandingsMemberDetail?> GetMemberDetailAsync(
        Guid memberId,
        Guid? monthSheetId,
        bool includeDeactivated,
        bool includeHidden,
        CancellationToken cancellationToken = default)
    {
        var users = await EligibleUsersAsync(includeDeactivated, cancellationToken);
        var member = users.FirstOrDefault(user => user.Id == memberId);
        if (member is null)
        {
            return null;
        }

        var entries = await db.PointEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == memberId)
            .Select(entry => new LedgerRow(
                entry.MonthSheetId,
                entry.MonthSheet!.Year,
                entry.MonthSheet.Month,
                entry.MonthSheet.Status,
                entry.Category,
                entry.SourceId,
                entry.FinalPoints,
                entry.Reason,
                entry.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var monthRows = entries
            .GroupBy(entry => new
            {
                entry.MonthSheetId,
                entry.Year,
                entry.Month,
                entry.Status
            })
            .Select(group => new StandingsMonthRow(
                group.Key.MonthSheetId,
                group.Key.Year,
                group.Key.Month,
                MonthLabels.Format(group.Key.Year, group.Key.Month),
                group.Key.Status,
                StandingsRules.Tally(
                    group.Select(entry => new StandingPoint(entry.Category, entry.FinalPoints)))))
            .OrderByDescending(row => row.Year)
            .ThenByDescending(row => row.Month)
            .ToList();

        var allMonths = await months.GetListAsync(cancellationToken);
        var selected = monthSheetId is { } requested
            ? allMonths.FirstOrDefault(month => month.Id == requested)
            : null;

        selected ??= await GetOpenMonthAsync(cancellationToken);
        selected ??= allMonths.FirstOrDefault();

        var info = new StandingsMemberInfo(
            member.Id,
            member.DisplayName,
            member.Status,
            member.CreatedAtUtc);

        if (selected is null)
        {
            return new StandingsMemberDetail(info, monthRows, null);
        }

        var monthEntries = entries
            .Where(entry => entry.MonthSheetId == selected.Id)
            .ToList();

        var monthRow = new StandingsMonthRow(
            selected.Id,
            selected.Year,
            selected.Month,
            selected.Label,
            selected.Status,
            StandingsRules.Tally(
                monthEntries.Select(entry => new StandingPoint(entry.Category, entry.FinalPoints))));

        var sheets = await BuildSheetsAsync(memberId, selected.Id, monthEntries, includeHidden, cancellationToken);
        var tasks = await BuildTasksAsync(memberId, selected.Id, includeHidden, cancellationToken);
        var bonuses = monthEntries
            .Where(entry => entry.Category is PointCategory.Bonus or PointCategory.Adjustment)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Select(entry => new StandingsBonusRow(
                entry.Category,
                entry.Reason,
                entry.FinalPoints,
                entry.CreatedAtUtc))
            .ToList();

        return new StandingsMemberDetail(
            info,
            monthRows,
            new StandingsMonthDetail(monthRow, sheets, tasks, bonuses));
    }

    public async Task<StandingsContext> GetContextAsync(
        Guid viewerId,
        CancellationToken cancellationToken = default)
    {
        var sheet = await db.DsSheets
            .AsNoTracking()
            .Where(row => row.IsActive && !row.IsHidden)
            .OrderByDescending(row => row.MonthSheet!.Year)
            .ThenByDescending(row => row.MonthSheet!.Month)
            .ThenBy(row => row.SortOrder)
            .Select(row => new
            {
                row.Id,
                row.Title,
                row.DueAtUtc,
                row.MonthSheet!.Year,
                row.MonthSheet.Month
            })
            .FirstOrDefaultAsync(cancellationToken);

        StandingsDsContext? sheetContext = null;
        if (sheet is not null)
        {
            var total = await db.DsProblems
                .AsNoTracking()
                .CountAsync(problem => problem.DsSheetId == sheet.Id, cancellationToken);

            var solveIds = await db.DsSolves
                .AsNoTracking()
                .Where(solve => solve.UserId == viewerId && solve.DsProblem!.DsSheetId == sheet.Id)
                .Select(solve => solve.Id)
                .ToListAsync(cancellationToken);

            var points = solveIds.Count == 0
                ? 0
                : await db.PointEntries
                    .AsNoTracking()
                    .Where(entry =>
                        entry.UserId == viewerId
                        && entry.Category == PointCategory.DsSolve
                        && entry.SourceId != null
                        && solveIds.Contains(entry.SourceId.Value))
                    .SumAsync(entry => (int?)entry.FinalPoints, cancellationToken) ?? 0;

            sheetContext = new StandingsDsContext(
                sheet.Id,
                sheet.Title,
                MonthLabels.Format(sheet.Year, sheet.Month),
                sheet.DueAtUtc,
                solveIds.Count,
                total,
                points);
        }

        var task = await db.CSharpTasks
            .AsNoTracking()
            .Where(row => row.IsActive && !row.IsHidden)
            .OrderByDescending(row => row.MonthSheet!.Year)
            .ThenByDescending(row => row.MonthSheet!.Month)
            .ThenBy(row => row.SortOrder)
            .Select(row => new { row.Id, row.Title, row.DueAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        StandingsTaskContext? taskContext = null;
        if (task is not null)
        {
            var attempts = await LoadAttemptsAsync(viewerId, task.Id, cancellationToken);
            var best = Creditable(attempts);
            var latest = attempts.OrderByDescending(attempt => attempt.AttemptNo).FirstOrDefault();
            var shown = best ?? latest;

            taskContext = new StandingsTaskContext(
                task.Id,
                task.Title,
                task.DueAtUtc,
                TaskStatusLabel(shown),
                best is null ? null : EffectivePoints(best),
                attempts.Count,
                shown?.IsLate ?? false);
        }

        return new StandingsContext(sheetContext, taskContext);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(
        Guid viewerId,
        CancellationToken cancellationToken = default)
    {
        var openMonth = await GetOpenMonthAsync(cancellationToken);
        var summary = openMonth is null
            ? null
            : await GetSummaryAsync(openMonth.Id, StandingsTab.All, includeDeactivated: false, cancellationToken);

        var row = summary?.Rows.FirstOrDefault(standing => standing.UserId == viewerId);
        var context = await GetContextAsync(viewerId, cancellationToken);
        var alerts = await BuildAlertsAsync(viewerId, context.Task, cancellationToken);

        return new DashboardSnapshot(
            openMonth?.Label ?? MonthLabels.Format(timeZone.NowLocal().Year, timeZone.NowLocal().Month),
            row?.Totals.Total ?? 0,
            row?.Rank,
            summary?.ParticipantCount ?? 0,
            context.DsSheet,
            context.Task,
            alerts);
    }

    private async Task<IReadOnlyList<StandingsSheetRow>> BuildSheetsAsync(
        Guid memberId,
        Guid monthSheetId,
        IReadOnlyList<LedgerRow> monthEntries,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        var sheets = await db.DsSheets
            .AsNoTracking()
            .Where(sheet => sheet.MonthSheetId == monthSheetId && (includeHidden || !sheet.IsHidden))
            .OrderBy(sheet => sheet.SortOrder)
            .ThenBy(sheet => sheet.Title)
            .Select(sheet => new { sheet.Id, sheet.Title })
            .ToListAsync(cancellationToken);

        if (sheets.Count == 0)
        {
            return [];
        }

        var sheetIds = sheets.Select(sheet => sheet.Id).ToList();

        var totals = await db.DsProblems
            .AsNoTracking()
            .Where(problem => sheetIds.Contains(problem.DsSheetId))
            .GroupBy(problem => problem.DsSheetId)
            .Select(group => new { SheetId = group.Key, Total = group.Count() })
            .ToDictionaryAsync(row => row.SheetId, row => row.Total, cancellationToken);

        var solveEntries = monthEntries
            .Where(entry => entry.Category == PointCategory.DsSolve && entry.SourceId is not null)
            .ToList();

        var solveIds = solveEntries
            .Select(entry => entry.SourceId!.Value)
            .Distinct()
            .ToList();

        var solveSheets = solveIds.Count == 0
            ? new List<SolveSheetRow>()
            : await db.DsSolves
                .AsNoTracking()
                .Where(solve => solveIds.Contains(solve.Id) && solve.UserId == memberId)
                .Select(solve => new SolveSheetRow(solve.Id, solve.DsProblem!.DsSheetId))
                .ToListAsync(cancellationToken);

        var pointsBySolve = solveEntries
            .GroupBy(entry => entry.SourceId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.FinalPoints));

        var solvedBySheet = solveSheets
            .Where(solve => sheetIds.Contains(solve.SheetId))
            .GroupBy(solve => solve.SheetId)
            .ToDictionary(group => group.Key, group => group.Count());

        var pointsBySheet = solveSheets
            .Where(solve => sheetIds.Contains(solve.SheetId))
            .GroupBy(solve => solve.SheetId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(solve => pointsBySolve.GetValueOrDefault(solve.Id)));

        return sheets
            .Select(sheet => new StandingsSheetRow(
                sheet.Id,
                sheet.Title,
                solvedBySheet.GetValueOrDefault(sheet.Id),
                totals.GetValueOrDefault(sheet.Id),
                pointsBySheet.GetValueOrDefault(sheet.Id)))
            .ToList();
    }

    private async Task<IReadOnlyList<StandingsTaskRow>> BuildTasksAsync(
        Guid memberId,
        Guid monthSheetId,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        var tasks = await db.CSharpTasks
            .AsNoTracking()
            .Where(task => task.MonthSheetId == monthSheetId && (includeHidden || !task.IsHidden))
            .OrderBy(task => task.CreatedAtUtc)
            .ThenBy(task => task.Title)
            .Select(task => new { task.Id, task.Title })
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            return [];
        }

        var taskIds = tasks.Select(task => task.Id).ToList();

        var submissions = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.UserId == memberId && taskIds.Contains(submission.TaskId))
            .Select(submission => new AttemptState(
                submission.Id,
                submission.TaskId,
                submission.AttemptNo,
                submission.SubmittedAtUtc,
                submission.IsLate,
                submission.Status,
                submission.Score,
                submission.IsManualScore))
            .ToListAsync(cancellationToken);

        return tasks
            .Select(task =>
            {
                var attempts = submissions
                    .Where(attempt => attempt.TaskId == task.Id)
                    .ToList();

                var best = Creditable(attempts);
                var shown = best ?? attempts.OrderByDescending(attempt => attempt.AttemptNo).FirstOrDefault();

                return new StandingsTaskRow(
                    task.Id,
                    task.Title,
                    best?.Id,
                    attempts.Count,
                    best?.Score,
                    best is null ? 0 : EffectivePoints(best),
                    best?.IsLate ?? false,
                    shown?.Status);
            })
            .ToList();
    }

    private async Task<List<AttemptState>> LoadAttemptsAsync(
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken) =>
        await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.UserId == userId && submission.TaskId == taskId)
            .OrderBy(submission => submission.AttemptNo)
            .Select(submission => new AttemptState(
                submission.Id,
                submission.TaskId,
                submission.AttemptNo,
                submission.SubmittedAtUtc,
                submission.IsLate,
                submission.Status,
                submission.Score,
                submission.IsManualScore))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<DashboardAlert>> BuildAlertsAsync(
        Guid viewerId,
        StandingsTaskContext? activeTask,
        CancellationToken cancellationToken)
    {
        var alerts = new List<DashboardAlert>();

        var viewer = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == viewerId)
            .Select(user => new { user.LeetCodeHandle, user.CodeforcesHandle })
            .FirstOrDefaultAsync(cancellationToken);

        if (viewer is not null
            && !await IsBoardAsync(viewerId, cancellationToken)
            && (string.IsNullOrWhiteSpace(viewer.LeetCodeHandle)
                || string.IsNullOrWhiteSpace(viewer.CodeforcesHandle)))
        {
            alerts.Add(new DashboardAlert(
                "handle",
                "Add both platform handles",
                "DS sync can only credit LeetCode and Codeforces when both handles are set.",
                "/settings",
                "Open settings"));
        }

        var failed = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.UserId == viewerId && submission.Status == SubmissionStatus.Failed)
            .OrderByDescending(submission => submission.SubmittedAtUtc)
            .Select(submission => new { submission.TaskId, Title = submission.Task!.Title })
            .FirstOrDefaultAsync(cancellationToken);

        if (failed is not null)
        {
            alerts.Add(new DashboardAlert(
                "failed",
                "A submission could not be graded",
                $"Your work for {failed.Title} is saved. The board can retry grading.",
                $"/tasks/{failed.TaskId}",
                "Open task"));
        }

        if (activeTask is not null
            && activeTask.Score is { } score
            && score < ICSharpTaskService.MaxPoints
            && !activeTask.IsLate
            && activeTask.DueAtUtc > timeZone.UtcNow)
        {
            alerts.Add(new DashboardAlert(
                "improve",
                "Your current task can be improved",
                $"You scored {score}/{ICSharpTaskService.MaxPoints} on {activeTask.Title}. A stronger attempt still counts before the deadline.",
                $"/tasks/{activeTask.TaskId}",
                "Open task"));
        }

        return alerts;
    }

    private async Task<IReadOnlyList<EligibleUser>> EligibleUsersAsync(
        bool includeDeactivated,
        CancellationToken cancellationToken)
    {
        var boardRoleId = await BoardRoleIdAsync(cancellationToken);

        var query = db.Users
            .AsNoTracking()
            .Where(user => user.Status == UserStatus.Active
                || (includeDeactivated && user.Status == UserStatus.Deactivated));

        if (boardRoleId is { } roleId)
        {
            query = query.Where(user =>
                !db.UserRoles.Any(link => link.UserId == user.Id && link.RoleId == roleId));
        }

        return await query
            .Select(user => new EligibleUser(
                user.Id,
                user.DisplayName,
                user.Status,
                user.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> IsBoardAsync(Guid userId, CancellationToken cancellationToken)
    {
        var boardRoleId = await BoardRoleIdAsync(cancellationToken);
        return boardRoleId is { } roleId
            && await db.UserRoles
                .AsNoTracking()
                .AnyAsync(link => link.UserId == userId && link.RoleId == roleId, cancellationToken);
    }

    private async Task<Guid?> BoardRoleIdAsync(CancellationToken cancellationToken) =>
        await db.Roles
            .AsNoTracking()
            .Where(role => role.Name == Roles.Board)
            .Select(role => (Guid?)role.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static AttemptState? Creditable(IEnumerable<AttemptState> attempts) =>
        attempts
            .Where(attempt => attempt.Status == SubmissionStatus.Graded && attempt.Score is not null)
            .OrderByDescending(attempt => EffectivePoints(attempt))
            .ThenBy(attempt => attempt.SubmittedAtUtc)
            .ThenBy(attempt => attempt.AttemptNo)
            .FirstOrDefault();

    private static int EffectivePoints(AttemptState attempt) =>
        SubmissionScoring.EffectivePoints(attempt.Score, attempt.IsLate, attempt.IsManualScore) ?? 0;

    private static string TaskStatusLabel(AttemptState? attempt) => attempt?.Status switch
    {
        null => "Not started",
        SubmissionStatus.Graded => attempt.Score is { } score ? $"Graded · {score}/{ICSharpTaskService.MaxPoints}" : "Graded",
        SubmissionStatus.Grading => "Grading",
        SubmissionStatus.Failed => "Grading failed",
        _ => "Submitted"
    };

    private sealed record EligibleUser(
        Guid Id,
        string DisplayName,
        UserStatus Status,
        DateTimeOffset CreatedAtUtc);

    private sealed record SolveSheetRow(Guid Id, Guid SheetId);

    private sealed record AttemptState(
        Guid Id,
        Guid TaskId,
        int AttemptNo,
        DateTimeOffset SubmittedAtUtc,
        bool IsLate,
        SubmissionStatus Status,
        int? Score,
        bool IsManualScore);

    private sealed record LedgerRow(
        Guid MonthSheetId,
        int Year,
        int Month,
        MonthStatus Status,
        PointCategory Category,
        Guid? SourceId,
        int FinalPoints,
        string? Reason,
        DateTimeOffset CreatedAtUtc);
}