using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Standings;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.ViewModels.Board;
using OscCommittee.Web.ViewModels.Shared;
using OscCommittee.Web.ViewModels.Standings;

namespace OscCommittee.Web.Controllers;

/// <summary>
/// The community standings (PLAN §9): summary and detailed views, three tabs, board filters,
/// month and member selectors. Nothing here exposes code or attempt contents.
/// </summary>
[Route("standings")]
[Authorize(Policy = "Member")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StandingsController(
    IStandingsService standings,
    ITimeZoneService timeZone) : Controller
{
    private const int PageSize = 25;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? tab = null,
        string? view = null,
        Guid? member = null,
        string? month = null,
        int page = 1,
        bool includeDeactivated = false,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Standings";

        var isBoard = User.IsInRole(Roles.Board);
        includeDeactivated &= isBoard;

        var selectedTab = StandingsQuery.ParseTab(tab);
        var selectedView = StandingsQuery.ParseView(view);
        var months = await standings.GetMonthsAsync(cancellationToken);

        // Members only ever see the public ranking on this page; their own ledger lives at /points.
        if (selectedView == StandingsView.Detail && !isBoard)
        {
            return Redirect("/points" + (selectedTab == StandingsTab.All
                ? string.Empty
                : $"?tab={StandingsQuery.TabSlug(selectedTab)}"));
        }

        Guid? selectedMemberId = null;
        (int Year, int Month)? selectedMonthValue = null;

        StandingsSummaryViewModel? summary = null;
        StandingsDetailViewModel? detail = null;

        if (selectedView == StandingsView.Summary)
        {
            summary = await BuildSummaryAsync(
                selectedTab,
                includeDeactivated,
                page,
                isBoard,
                cancellationToken);
        }
        else
        {
            var memberOptions = await standings.GetMemberOptionsAsync(includeDeactivated, cancellationToken);
            var selectedMember = member is { } requested
                ? memberOptions.FirstOrDefault(option => option.Id == requested)
                : null;

            selectedMember ??= memberOptions.FirstOrDefault(option => option.Id == CurrentUserId);
            selectedMember ??= memberOptions.FirstOrDefault();

            if (selectedMember is not null)
            {
                selectedMemberId = selectedMember.Id;

                var requestedMonth = ParseMonth(month);
                var requestedMonthId = requestedMonth is { } value
                    ? months.FirstOrDefault(sheet => sheet.Year == value.Year && sheet.Month == value.Month)?.Id
                    : null;

                var data = await standings.GetMemberDetailAsync(
                    selectedMember.Id,
                    requestedMonthId,
                    includeDeactivated,
                    includeHidden: isBoard,
                    cancellationToken);

                if (data is not null)
                {
                    selectedMonthValue = data.SelectedMonth is { } selected
                        ? (selected.Row.Year, selected.Row.Month)
                        : null;

                    detail = BuildDetail(
                        data,
                        memberOptions,
                        months,
                        selectedTab,
                        includeDeactivated,
                        isBoard,
                        selectedMemberId.Value,
                        selectedMonthValue,
                        selfView: false);
                }
            }
        }

        string BuildHref(
            StandingsTab linkTab,
            StandingsView linkView,
            Guid? linkMember,
            (int Year, int Month)? linkMonth,
            bool includeDeactivatedLink,
            int? pageNumber = null)
        {
            var query = new List<string>
            {
                $"tab={StandingsQuery.TabSlug(linkTab)}",
                $"view={StandingsQuery.ViewSlug(linkView)}"
            };

            if (linkView == StandingsView.Detail)
            {
                if (linkMember is { } memberId)
                {
                    query.Add($"member={memberId}");
                }

                if (linkMonth is { } value)
                {
                    query.Add($"month={MonthValue(value.Year, value.Month)}");
                }
            }

            if (includeDeactivatedLink)
            {
                query.Add("includeDeactivated=true");
            }

            if (pageNumber is { } number && number > 1)
            {
                query.Add($"page={number}");
            }

            return "/standings?" + string.Join("&", query);
        }

        var tabs = new List<StandingsSwitchLink>
        {
            new(
                "All",
                BuildHref(StandingsTab.All, selectedView, selectedMemberId, selectedMonthValue, includeDeactivated),
                selectedTab == StandingsTab.All),
            new(
                "DS",
                BuildHref(StandingsTab.Ds, selectedView, selectedMemberId, selectedMonthValue, includeDeactivated),
                selectedTab == StandingsTab.Ds),
            new(
                "Tasks",
                BuildHref(StandingsTab.Tasks, selectedView, selectedMemberId, selectedMonthValue, includeDeactivated),
                selectedTab == StandingsTab.Tasks)
        };

        return View(new StandingsViewModel
        {
            Tab = selectedTab,
            View = selectedView,
            Tabs = tabs,
            IsBoard = isBoard,
            IncludeDeactivated = includeDeactivated,
            IncludeDeactivatedHref = BuildHref(
                selectedTab,
                selectedView,
                selectedMemberId,
                selectedMonthValue,
                includeDeactivatedLink: true),
            ExcludeDeactivatedHref = BuildHref(
                selectedTab,
                selectedView,
                selectedMemberId,
                selectedMonthValue,
                includeDeactivatedLink: false),
            RankingNote = selectedTab switch
            {
                StandingsTab.Ds => "Ranked by DS points",
                StandingsTab.Tasks => "Ranked by task points",
                _ => "Ranked by total points"
            },
            Summary = summary,
            Detail = detail
        });
    }

    [HttpGet("/points")]
    public async Task<IActionResult> Points(
        string? tab = null,
        string? month = null,
        CancellationToken cancellationToken = default)
    {
        // The board has no ledger of its own; it reads members through the standings detail.
        if (User.IsInRole(Roles.Board))
        {
            return Redirect("/standings");
        }

        ViewData["Title"] = "Points";

        var selectedTab = StandingsQuery.ParseTab(tab);
        var months = await standings.GetMonthsAsync(cancellationToken);
        var requestedMonth = ParseMonth(month);
        var requestedMonthId = requestedMonth is { } value
            ? months.FirstOrDefault(sheet => sheet.Year == value.Year && sheet.Month == value.Month)?.Id
            : null;

        var data = await standings.GetMemberDetailAsync(
            CurrentUserId,
            requestedMonthId,
            includeDeactivated: false,
            includeHidden: false,
            cancellationToken);

        if (data is null)
        {
            return NotFound();
        }

        (int Year, int Month)? selectedMonthValue = data.SelectedMonth is { } selected
            ? (selected.Row.Year, selected.Row.Month)
            : null;

        var detail = BuildDetail(
            data,
            [],
            months,
            selectedTab,
            includeDeactivated: false,
            isBoard: false,
            data.Member.Id,
            selectedMonthValue,
            selfView: true);

        return View("~/Views/Points/Index.cshtml", detail);
    }

    private async Task<StandingsSummaryViewModel> BuildSummaryAsync(
        StandingsTab tab,
        bool includeDeactivated,
        int page,
        bool isBoard,
        CancellationToken cancellationToken)
    {
        var openMonth = await standings.GetOpenMonthAsync(cancellationToken);
        if (openMonth is null)
        {
            return new StandingsSummaryViewModel { HasMonth = false };
        }

        var data = await standings.GetSummaryAsync(openMonth.Id, tab, includeDeactivated, cancellationToken);

        // The context strip is the viewer's own progress. Board accounts are not ranked and do not
        // submit, so showing their empty sheet and task cards would only be noise.
        var context = isBoard
            ? null
            : await standings.GetContextAsync(CurrentUserId, cancellationToken);

        var totalPages = Math.Max(1, (int)Math.Ceiling(data.Rows.Count / (double)PageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);
        var currentUserId = CurrentUserId;

        var rows = data.Rows
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(row => new StandingsRowViewModel
            {
                Rank = row.Rank,
                UserId = row.UserId,
                DisplayName = row.DisplayName,
                Initials = UserStatusView.Initials(row.DisplayName),
                IsCurrentUser = row.UserId == currentUserId,
                DsPoints = row.Totals.DsPoints,
                TaskPoints = row.Totals.TaskPoints,
                BonusPoints = row.Totals.BonusPoints,
                Total = row.Totals.Total,
                DsSolved = row.Totals.DsSolved,
                TasksSolved = row.Totals.TasksSolved,
                DetailHref = isBoard
                    ? DetailHref(tab, row.UserId, openMonth, includeDeactivated)
                    : row.UserId == currentUserId ? "/points" : string.Empty
            })
            .ToList();

        var paginationHref = $"/standings?tab={StandingsQuery.TabSlug(tab)}&view=summary"
            + (includeDeactivated ? "&includeDeactivated=true" : string.Empty)
            + "&page={0}";

        return new StandingsSummaryViewModel
        {
            Tab = tab,
            MonthSheetId = openMonth.Id,
            MonthLabel = openMonth.Label,
            HasMonth = true,
            Rows = rows,
            ParticipantCount = data.ParticipantCount,
            Pagination = data.Rows.Count > PageSize
                ? new PaginationModel
                {
                    Page = currentPage,
                    TotalPages = totalPages,
                    HrefTemplate = paginationHref
                }
                : null,
            Context = context is null ? null : MapContext(context)
        };

        string DetailHref(StandingsTab tab, Guid userId, MonthSheetSummary open, bool include)
        {
            var query = $"/standings?tab={StandingsQuery.TabSlug(tab)}&view=detail&member={userId}"
                + $"&month={MonthValue(open.Year, open.Month)}";

            return include ? query + "&includeDeactivated=true" : query;
        }
    }

    private StandingsDetailViewModel BuildDetail(
        StandingsMemberDetail data,
        IReadOnlyList<StandingsMemberOption> memberOptions,
        IReadOnlyList<MonthSheetSummary> months,
        StandingsTab tab,
        bool includeDeactivated,
        bool isBoard,
        Guid selectedMemberId,
        (int Year, int Month)? selectedMonthValue,
        bool selfView)
    {
        var selectedMonthId = data.SelectedMonth?.Row.MonthSheetId;

        var memberChoices = memberOptions
            .Select(option => new StandingsMemberChoice(
                option.Id,
                option.Status == UserStatus.Deactivated
                    ? $"{option.DisplayName} (deactivated)"
                    : option.DisplayName,
                option.Id == selectedMemberId,
                option.Status))
            .ToList();

        var monthChoices = months
            .Select(sheet => new StandingsMonthChoice(
                sheet.Id,
                MonthValue(sheet.Year, sheet.Month),
                sheet.Label,
                sheet.Id == selectedMonthId))
            .ToList();

        var monthRows = data.Months
            .Select(row => new StandingsMonthRowViewModel
            {
                MonthSheetId = row.MonthSheetId,
                Label = row.Label,
                Status = row.Status,
                IsSelected = row.MonthSheetId == selectedMonthId,
                Href = DetailHref(tab, row.Year, row.Month, includeDeactivated),
                DsSolved = row.Totals.DsSolved,
                DsPoints = row.Totals.DsPoints,
                TasksSolved = row.Totals.TasksSolved,
                TaskPoints = row.Totals.TaskPoints,
                BonusPoints = row.Totals.BonusPoints
            })
            .ToList();

        var selected = data.SelectedMonth;
        var totals = selected is null
            ? null
            : new StandingsMonthTotalsViewModel
            {
                DsSolved = selected.Row.Totals.DsSolved,
                DsPoints = selected.Row.Totals.DsPoints,
                TasksSolved = selected.Row.Totals.TasksSolved,
                TaskPoints = selected.Row.Totals.TaskPoints,
                BonusPoints = selected.Row.Totals.BonusPoints
            };

        var sheets = selected?.Sheets
            .Select(row => new StandingsSheetRowViewModel
            {
                SheetId = row.SheetId,
                Title = row.Title,
                Solved = row.Solved,
                Total = row.Total,
                Points = row.Points
            })
            .ToList() ?? [];

        var tasks = selected?.Tasks
            .Select(row => new StandingsTaskRowViewModel
            {
                TaskId = row.TaskId,
                Title = row.Title,
                TaskHref = $"/tasks/{row.TaskId}",
                AttemptHref = isBoard && row.SubmissionId is { } submissionId
                    ? $"/board/submissions/{submissionId}"
                    : null,
                AttemptCount = row.AttemptCount,
                Score = row.Score,
                Points = row.Points,
                IsLate = row.IsLate,
                StatusLabel = TaskStatusLabel(row.Status),
                StatusBadgeClass = TaskStatusBadge(row.Status)
            })
            .ToList() ?? [];

        var bonuses = selected?.Bonuses
            .Select(row => new StandingsBonusRowViewModel
            {
                Label = row.Category == PointCategory.Bonus ? "Bonus" : "Adjustment",
                BadgeClass = row.Category == PointCategory.Bonus ? "badge-graded" : "badge-hidden",
                Reason = row.Reason,
                Points = row.Points,
                AddedLabel = timeZone.FormatLocal(row.CreatedAtUtc, "d MMM yyyy"),
                IsManual = row.Category == PointCategory.Adjustment
            })
            .ToList() ?? [];

        return new StandingsDetailViewModel
        {
            Members = memberChoices,
            SelectedMemberId = data.Member.Id,
            SelectedMemberName = data.Member.DisplayName,
            SelectedMemberInitials = UserStatusView.Initials(data.Member.DisplayName),
            SelectedMemberStatus = data.Member.Status,
            SelectedMemberStatusLabel = UserStatusView.Label(data.Member.Status),
            SelectedMemberStatusBadgeClass = UserStatusView.BadgeClass(data.Member.Status),
            RegisteredLabel = timeZone.FormatLocal(data.Member.RegisteredAtUtc, "d MMM yyyy"),
            Months = monthChoices,
            SelectedMonthId = selectedMonthId,
            SelectedMonthLabel = selected?.Row.Label ?? string.Empty,
            SelectedMonthIsOpen = selected?.Row.Status == MonthStatus.Open,
            SelectedTotals = totals,
            MonthRows = monthRows,
            Sheets = sheets,
            Tasks = tasks,
            Bonuses = bonuses,
            TabSlug = StandingsQuery.TabSlug(tab),
            IncludeDeactivated = includeDeactivated,
            CanPickMember = !selfView,
            FilterHref = selfView ? "/points" : "/standings"
        };

        string DetailHref(StandingsTab linkTab, int year, int month, bool include)
        {
            if (selfView)
            {
                return $"/points?tab={StandingsQuery.TabSlug(linkTab)}&month={MonthValue(year, month)}";
            }

            var query = $"/standings?tab={StandingsQuery.TabSlug(linkTab)}&view=detail"
                + $"&member={selectedMemberId}&month={MonthValue(year, month)}";

            return include ? query + "&includeDeactivated=true" : query;
        }
    }

    private StandingsContextViewModel MapContext(StandingsContext context) => new()
    {
        DsSheet = context.DsSheet is { } sheet
            ? new StandingsDsCardViewModel
            {
                Id = sheet.SheetId,
                Title = sheet.Title,
                MonthLabel = sheet.MonthLabel,
                DueLabel = timeZone.FormatLocal(sheet.DueAtUtc, "d MMM"),
                DueIso = sheet.DueAtUtc.ToString("O", CultureInfo.InvariantCulture),
                IsOverdue = sheet.DueAtUtc <= timeZone.UtcNow,
                Solved = sheet.Solved,
                Total = sheet.Total,
                Points = sheet.Points
            }
            : null,
        Task = context.Task is { } task
            ? new StandingsTaskCardViewModel
            {
                Id = task.TaskId,
                Title = task.Title,
                DueLabel = timeZone.FormatLocal(task.DueAtUtc, "d MMM"),
                DueIso = task.DueAtUtc.ToString("O", CultureInfo.InvariantCulture),
                IsOverdue = task.DueAtUtc <= timeZone.UtcNow,
                StatusLabel = task.StatusLabel,
                Score = task.Score,
                Attempts = task.Attempts,
                IsLate = task.IsLate,
                MaxPoints = ICSharpTaskService.MaxPoints
            }
            : null
    };

    private static string TaskStatusLabel(SubmissionStatus? status) => status switch
    {
        null => "Not started",
        SubmissionStatus.Graded => "Graded",
        SubmissionStatus.Grading => "Grading",
        SubmissionStatus.Failed => "Grading failed",
        _ => "Submitted"
    };

    private static string TaskStatusBadge(SubmissionStatus? status) => status switch
    {
        SubmissionStatus.Graded => "badge-graded",
        SubmissionStatus.Failed => "badge-hard",
        SubmissionStatus.Grading => "badge-pending",
        SubmissionStatus.Uploaded => "badge-pending",
        _ => "badge-hidden"
    };

    private static string MonthValue(int year, int month) =>
        $"{year:D4}-{month:D2}";

    private static (int Year, int Month)? ParseMonth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || year is < 2000 or > 2100
            || month is < 1 or > 12)
        {
            return null;
        }

        return (year, month);
    }

    private Guid CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id)
                ? id
                : throw new InvalidOperationException("The Member policy ran without a user id claim.");
        }
    }
}