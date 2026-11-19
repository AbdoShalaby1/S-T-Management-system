using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

[Route("board/months")]
public sealed class MonthsController(IMonthSheetService months, ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Months";

        // Opening the page is the lazy-creation trigger from PLAN §6.1.
        await months.GetOrCreateCurrentAsync(cancellationToken);

        var rows = await months.GetListAsync(cancellationToken);
        var local = timeZone.NowLocal();

        var items = rows.Select(row => new MonthRowViewModel
        {
            Id = row.Id,
            Label = row.Label,
            Status = row.Status,
            IsCurrent = row.Year == local.Year && row.Month == local.Month,
            OpenedLabel = timeZone.FormatLocal(row.OpenedAtUtc, "d MMM yyyy"),
            ClosedLabel = row.ClosedAtUtc is { } closed
                ? timeZone.FormatLocal(closed, "d MMM yyyy")
                : "—",
            ClosedByLabel = row.IsOpen ? "—" : row.ClosedByName ?? "Automatic"
        }).ToList();

        return View(new MonthsViewModel
        {
            Months = items,
            OpenCount = rows.Count(row => row.IsOpen),
            ClosedCount = rows.Count(row => !row.IsOpen),
            CurrentMonthLabel = timeZone.CurrentMonthLabel()
        });
    }

    [HttpPost("{id:guid}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken)
    {
        Toast(await months.CloseAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/open")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(Guid id, CancellationToken cancellationToken)
    {
        Toast(await months.OpenAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }
}
