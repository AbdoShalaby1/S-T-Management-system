using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Points;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

[Route("board/members")]
public sealed class MembersController(
    IMemberAdminService members,
    IPointsService points,
    IMonthSheetService months,
    ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? status = null,
        bool missingHandles = false,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Members";

        var statusFilter = ParseStatus(status);
        var normalizedSort = NormalizeSort(sort);

        var rows = await members.GetMembersAsync(
            new MemberFilter(statusFilter, missingHandles, normalizedSort),
            cancellationToken);

        var counts = await members.GetCountsAsync(cancellationToken);
        var currentMonth = await months.GetOrCreateCurrentAsync(cancellationToken);
        var totals = await points.GetMonthTotalsAsync(currentMonth.Id, cancellationToken);

        var mapped = rows
            .Select(member => ToMemberRow(
                member,
                timeZone,
                totals.TryGetValue(member.Id, out var total) ? total : null))
            .ToList();

        if (normalizedSort == "points")
        {
            mapped = mapped
                .OrderByDescending(row => row.Points ?? int.MinValue)
                .ThenBy(row => row.DisplayName)
                .ToList();
        }

        return View(new MembersViewModel
        {
            Members = mapped,
            StatusFilter = statusFilter?.ToString(),
            MissingHandlesOnly = missingHandles,
            Sort = normalizedSort,
            TotalCount = counts.Total,
            ActiveCount = counts.Active,
            PendingCount = counts.Pending,
            DeactivatedCount = counts.Deactivated
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(id, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        ViewData["Title"] = member.DisplayName;

        var local = timeZone.NowLocal();
        var currentMonth = await months.GetOrCreateAsync(local.Year, local.Month, cancellationToken);
        var ledger = await points.GetMemberMonthsAsync(id, cancellationToken);

        return View(new MemberDetailViewModel
        {
            Id = member.Id,
            DisplayName = member.DisplayName,
            Email = member.Email,
            Initials = UserStatusView.Initials(member.DisplayName),
            Status = member.Status,
            IsBoard = member.IsBoard,
            LeetCodeHandle = member.LeetCodeHandle,
            CodeforcesHandle = member.CodeforcesHandle,
            JoinedLabel = timeZone.FormatLocal(member.JoinedAtUtc, "d MMM yyyy"),
            ApprovedLabel = member.ApprovedAtUtc is { } approved
                ? timeZone.FormatLocal(approved, "d MMM yyyy")
                : null,
            DeactivatedLabel = member.DeactivatedAtUtc is { } deactivated
                ? timeZone.FormatLocal(deactivated, "d MMM yyyy")
                : null,
            CurrentMonthSheetId = currentMonth.Id,
            CurrentMonthLabel = MonthLabels.Format(currentMonth.Year, currentMonth.Month),
            CurrentMonthPoints = ledger
                .Where(month => month.MonthSheetId == currentMonth.Id)
                .Sum(month => month.Total),
            AllTimePoints = ledger.Sum(month => month.Total),
            Months = ledger
        });
    }

    [HttpPost("{id:guid}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        Toast(await members.DeactivateAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        Toast(await members.RestoreAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    private static UserStatus? ParseStatus(string? status) =>
        Enum.TryParse<UserStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static string NormalizeSort(string? sort) => sort switch
    {
        "joined" => "joined",
        "status" => "status",
        "points" => "points",
        _ => "name"
    };
}
