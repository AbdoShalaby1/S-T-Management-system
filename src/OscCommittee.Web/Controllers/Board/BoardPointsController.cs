using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Points;
using OscCommittee.Web.Services.Settings;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

/// <summary>
/// The points ledger (PLAN §8.8): every member's totals, one member's problems, tasks and bonuses.
/// Bonuses are the only manual entry.
/// </summary>
[Route("board/points")]
public sealed class BoardPointsController(
    IPointsService points,
    IMemberAdminService members,
    IMonthSheetService months,
    ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Points";

        var current = await months.GetOrCreateCurrentAsync(cancellationToken);
        var listed = await members.GetMembersAsync(
            new MemberFilter(Sort: "name"),
            cancellationToken);

        var monthTotals = await points.GetMonthTotalsAsync(current.Id, cancellationToken);
        var allTimeTotals = await points.GetAllTimeTotalsAsync(cancellationToken);

        var rows = listed
            .Where(member => !member.IsBoard)
            .Select(member => new PointsIndexRow(
                member.Id,
                member.DisplayName,
                member.Email,
                UserStatusView.Initials(member.DisplayName),
                member.Status,
                monthTotals.GetValueOrDefault(member.Id),
                allTimeTotals.GetValueOrDefault(member.Id)))
            .OrderByDescending(row => row.MonthPoints)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return View(new PointsIndexViewModel
        {
            MonthLabel = MonthLabels.Format(current.Year, current.Month),
            Members = rows,
            MemberCount = rows.Count,
            MonthPoints = rows.Sum(row => row.MonthPoints),
            AllTimePoints = rows.Sum(row => row.AllTimePoints)
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Member(Guid id, CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(id, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        ViewData["Title"] = member.DisplayName;

        return View(await BuildMemberViewModelAsync(member, new PointsBonusForm(), cancellationToken));
    }

    /// <summary>Manual points are bonuses only; the ledger still totals them as earned points.</summary>
    [HttpPost("{id:guid}/bonus")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBonus(
        Guid id,
        PointsBonusForm form,
        CancellationToken cancellationToken)
    {
        var member = await members.FindAsync(id, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        ViewData["Title"] = member.DisplayName;

        if (form.MonthSheetId is null)
        {
            ModelState.AddModelError(nameof(form.MonthSheetId), "Pick a month.");
        }

        if (!ModelState.IsValid || form.MonthSheetId is null)
        {
            return View("Member", await BuildMemberViewModelAsync(member, form, cancellationToken));
        }

        var result = await points.AddBonusAsync(
            id,
            form.MonthSheetId.Value,
            form.Amount,
            form.Reason,
            CurrentUserId,
            cancellationToken);

        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View("Member", await BuildMemberViewModelAsync(member, form, cancellationToken));
        }

        Toast(result);
        return Redirect(Url.Action(nameof(Member), new { id }) + "#add");
    }

    private async Task<MemberPointsViewModel> BuildMemberViewModelAsync(
        MemberSummary member,
        PointsBonusForm form,
        CancellationToken cancellationToken)
    {
        var local = timeZone.NowLocal();
        var current = await months.GetOrCreateAsync(local.Year, local.Month, cancellationToken);

        var previousYear = local.Month == 1 ? local.Year - 1 : local.Year;
        var previousMonth = local.Month == 1 ? 12 : local.Month - 1;
        var previous = await months.GetOrCreateAsync(previousYear, previousMonth, cancellationToken);

        var ledger = await points.GetMemberMonthsAsync(member.Id, cancellationToken);
        var entries = ledger.SelectMany(month => month.Entries).ToList();

        form.MonthSheetId ??= current.Id;

        return new MemberPointsViewModel
        {
            Id = member.Id,
            DisplayName = member.DisplayName,
            Email = member.Email,
            Initials = UserStatusView.Initials(member.DisplayName),
            Status = member.Status,
            IsBoard = member.IsBoard,
            CurrentMonthLabel = MonthLabels.Format(current.Year, current.Month),
            CurrentMonthPoints = ledger
                .Where(month => month.MonthSheetId == current.Id)
                .Sum(month => month.Total),
            AllTimePoints = ledger.Sum(month => month.Total),
            ProblemCount = entries.Count(entry => entry.Category == PointCategory.DsSolve),
            TaskCount = entries.Count(entry => entry.Category == PointCategory.CSharpSubmission),
            Months = ledger,
            MonthOptions =
            [
                new PointsMonthOption(
                    current.Id,
                    MonthLabels.Format(current.Year, current.Month),
                    IsCurrent: true,
                    current.Status),
                new PointsMonthOption(
                    previous.Id,
                    MonthLabels.Format(previous.Year, previous.Month),
                    IsCurrent: false,
                    previous.Status)
            ],
            Bonus = form
        };
    }
}