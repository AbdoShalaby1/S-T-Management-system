using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Ds;
using OscCommittee.Web.Services.Standings;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.ViewModels.Ds;
using OscCommittee.Web.ViewModels.Home;

namespace OscCommittee.Web.Controllers;

[Authorize(Policy = "Member")]
public sealed class HomeController(
    IStandingsService standings,
    IDsSyncService sync,
    ITimeZoneService timeZone) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Board accounts are not ranked and have no member work of their own.
        if (User.IsInRole(Roles.Board))
        {
            return RedirectToAction("Index", "Board");
        }

        var snapshot = await standings.GetDashboardAsync(CurrentUserId, cancellationToken);
        var status = await sync.GetStatusAsync(CurrentUserId, cancellationToken);

        var model = new DashboardViewModel
        {
            MonthLabel = snapshot.MonthLabel,
            DisplayName = User.FindFirst(AppUserClaimsPrincipalFactory.DisplayNameClaim)?.Value
                ?? User.Identity?.Name
                ?? "Member",
            MonthTotal = snapshot.MonthTotal,
            Rank = snapshot.Rank,
            CohortSize = snapshot.CohortSize,
            IsBoard = User.IsInRole(Roles.Board),
            Sync = DsSyncButtonViewModel.From(status, "/"),
            ActiveDsSheet = snapshot.ActiveDsSheet is { } sheet
                ? new DashboardDsSheetViewModel
                {
                    Id = sheet.SheetId,
                    Title = sheet.Title,
                    MonthLabel = sheet.MonthLabel,
                    DueLabel = timeZone.FormatLocal(sheet.DueAtUtc, "d MMM"),
                    DueIso = sheet.DueAtUtc.ToString("O"),
                    IsOverdue = sheet.DueAtUtc <= timeZone.UtcNow,
                    Solved = sheet.Solved,
                    Total = sheet.Total,
                    Points = sheet.Points
                }
                : null,
            ActiveTask = snapshot.ActiveTask is { } task
                ? new DashboardTaskViewModel
                {
                    Id = task.TaskId,
                    Title = task.Title,
                    DueLabel = timeZone.FormatLocal(task.DueAtUtc, "d MMM"),
                    DueIso = task.DueAtUtc.ToString("O"),
                    IsOverdue = task.DueAtUtc <= timeZone.UtcNow,
                    StatusLabel = task.StatusLabel,
                    Score = task.Score,
                    Attempts = task.Attempts,
                    IsLate = task.IsLate,
                    MaxPoints = ICSharpTaskService.MaxPoints
                }
                : null,
            Alerts = snapshot.Alerts
                .Select(alert => new DashboardAlertViewModel
                {
                    Kind = alert.Kind,
                    Title = alert.Title,
                    Message = alert.Message,
                    Href = alert.Href,
                    ActionLabel = alert.ActionLabel
                })
                .ToList()
        };

        ViewData["Title"] = "Dashboard";
        return View(model);
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