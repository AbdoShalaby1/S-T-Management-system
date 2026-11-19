using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

[Route("board/approvals")]
public sealed class ApprovalsController(IMemberAdminService members, ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Approvals";

        var pending = await members.GetPendingAsync(50, cancellationToken);

        return View(new ApprovalsViewModel
        {
            Items = pending.Select(member => ToApprovalRow(member, timeZone)).ToList()
        });
    }

    [HttpPost("{id:guid}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        Toast(await members.ApproveAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>The note stays private: it is only written to the audit trail, never to the member.</summary>
    [HttpPost("{id:guid}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? note, CancellationToken cancellationToken)
    {
        Toast(await members.RejectAsync(id, CurrentUserId, note, cancellationToken));
        return RedirectToAction(nameof(Index));
    }
}
