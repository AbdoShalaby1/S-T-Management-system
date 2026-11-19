using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

[Route("board")]
public sealed class BoardController(
    IMemberAdminService members,
    IMonthSheetService months,
    ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Board";

        var recent = await members.GetPendingAsync(5, cancellationToken);
        var month = await months.GetOrCreateCurrentAsync(cancellationToken);

        return View(new BoardDashboardViewModel
        {
            MonthLabel = MonthLabels.Format(month.Year, month.Month),
            PendingCount = await members.CountPendingAsync(cancellationToken),
            ActiveMemberCount = await members.CountActiveMembersAsync(cancellationToken),
            RecentPending = recent.Select(member => ToApprovalRow(member, timeZone)).ToList()
        });
    }
}
