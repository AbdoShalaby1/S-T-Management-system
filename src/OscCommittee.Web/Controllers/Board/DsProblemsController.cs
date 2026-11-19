using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Ds;
using OscCommittee.Web.Services.Settings;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

/// <summary>Board-side problem management (PLAN §8.5): URL import, create, order, delete.</summary>
[Route("board/ds/problems")]
public sealed class DsProblemsController(
    IDsProblemService problems,
    IDsSheetService sheets,
    IDsCreditService credit,
    IMemberAdminService members,
    ISettingsService settings,
    ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("new")]
    public async Task<IActionResult> New(Guid? sheetId, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New DS problem";

        var presets = await settings.GetPointsPresetsAsync(cancellationToken);
        var form = new DsProblemFormViewModel
        {
            DsSheetId = sheetId,
            Presets = presets
        };

        return await RenderAsync(form, cancellationToken);
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        DsProblemFormViewModel form,
        string command,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New DS problem";

        if (command == "import")
        {
            return await ImportIntoFormAsync(form, cancellationToken);
        }

        if (!TryBuildRequest(form, out var request))
        {
            return await RenderAsync(form, cancellationToken);
        }

        var result = await problems.CreateAsync(request, CurrentUserId, cancellationToken);

        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return await RenderAsync(form, cancellationToken);
        }

        Toast(Result.Ok(result.Message));
        return RedirectToSheet(request.DsSheetId);
    }

    /// <summary>JSON metadata for the paste-a-URL island. The form still posts on no-JS.</summary>
    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(string? url, CancellationToken cancellationToken)
    {
        var result = await problems.PreviewAsync(url, cancellationToken);

        if (result.Failed || result.Value is null)
        {
            return BadRequest(new { ok = false, message = result.Message });
        }

        var preview = result.Value;

        return Ok(new
        {
            ok = true,
            platform = preview.Platform.ToString(),
            platformLabel = preview.Platform == Platform.LeetCode ? "LeetCode" : "Codeforces",
            externalId = preview.ExternalId,
            url = preview.CanonicalUrl,
            title = preview.Title,
            rating = preview.Rating,
            isPaidOnly = preview.IsPaidOnly,
            reference = preview.Reference,
            warning = preview.Warning
        });
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var row = await problems.FindAsync(id, cancellationToken);
        var result = await problems.DeleteAsync(id, CurrentUserId, cancellationToken);

        Toast(result);
        return row is null ? RedirectToAction("Index", "DsSheets") : RedirectToSheet(row.DsSheetId);
    }

    [HttpPost("{id:guid}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(Guid id, int direction, CancellationToken cancellationToken)
    {
        var row = await problems.FindAsync(id, cancellationToken);
        var result = await problems.MoveAsync(id, direction, CurrentUserId, cancellationToken);

        Toast(result);
        return row is null ? RedirectToAction("Index", "DsSheets") : RedirectToSheet(row.DsSheetId);
    }

    [HttpGet("{id:guid}/solvers")]
    public async Task<IActionResult> Solvers(Guid id, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Problem solvers";

        var row = await problems.FindAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        var solvers = await problems.GetSolversAsync(id, cancellationToken);
        var active = await members.GetMembersAsync(
            new MemberFilter(Status: UserStatus.Active),
            cancellationToken);

        return View(new DsProblemSolversViewModel
        {
            ProblemId = row.Id,
            Title = row.Title,
            ProblemUrl = row.Url,
            SheetTitle = row.SheetTitle,
            PlatformLabel = row.Platform == Platform.LeetCode ? "LeetCode" : "Codeforces",
            Points = row.Points,
            Solvers = solvers
                .Select(solver => new DsSolverRowViewModel(
                    solver.UserId,
                    solver.DisplayName,
                    timeZone.FormatLocal(solver.SolvedAtUtc, "d MMM yyyy, HH:mm"),
                    string.IsNullOrWhiteSpace(solver.Language) ? "—" : solver.Language,
                    solver.Points,
                    solver.IsLate,
                    solver.IsManual,
                    solver.PlatformUrl))
                .ToList(),
            Members = active
                .Where(member => !member.IsBoard)
                .Select(member => new DsMemberOption(member.Id, MemberLabel(member)))
                .ToList(),
            DefaultSolvedAtLocal = timeZone.NowLocal().ToString("yyyy-MM-ddTHH:mm")
        });
    }

    /// <summary>Manual credit for accepted work outside LeetCode's twenty-row window.</summary>
    [HttpPost("{id:guid}/credit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Credit(
        Guid id,
        Guid memberId,
        DateTime solvedAtLocal,
        string? language,
        string? reason,
        CancellationToken cancellationToken)
    {
        var row = await problems.FindAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        if (memberId == Guid.Empty)
        {
            Toast(Result.Fail("no_member", "Pick the member you are crediting."));
            return RedirectToAction(nameof(Solvers), new { id });
        }

        if (solvedAtLocal == default)
        {
            Toast(Result.Fail("no_time", "Enter when the solve happened."));
            return RedirectToAction(nameof(Solvers), new { id });
        }

        var member = await members.FindAsync(memberId, cancellationToken);
        if (member is null || member.IsBoard)
        {
            Toast(Result.Fail("no_member", "That member is no longer active."));
            return RedirectToAction(nameof(Solvers), new { id });
        }

        var result = await credit.CreditAsync(
            new DsCreditRequest(
                memberId,
                row.Id,
                $"manual:{Guid.NewGuid():N}",
                timeZone.FromLocal(solvedAtLocal),
                string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
                row.Url,
                IsManual: true,
                CreditedByUserId: CurrentUserId,
                Reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()),
            cancellationToken);

        Toast(result.Credited
            ? Result.Ok(result.Message)
            : Result.Fail(result.ErrorCode ?? "credit_failed", result.Message));
        return RedirectToAction(nameof(Solvers), new { id });
    }

    private static string MemberLabel(MemberSummary member)
    {
        var handles = new[] { member.LeetCodeHandle, member.CodeforcesHandle }
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .ToList();

        return handles.Count == 0
            ? member.DisplayName
            : $"{member.DisplayName} · {string.Join(" / ", handles)}";
    }

    private async Task<IActionResult> ImportIntoFormAsync(
        DsProblemFormViewModel form,
        CancellationToken cancellationToken)
    {
        var preview = await problems.PreviewAsync(form.Url, cancellationToken);

        if (preview.Failed || preview.Value is null)
        {
            ModelState.AddModelError(nameof(form.Url), preview.Message);
            return await RenderAsync(form, cancellationToken);
        }

        var value = preview.Value;

        form.Platform = value.Platform;
        form.ExternalId = value.ExternalId;
        form.Url = value.CanonicalUrl;
        form.Title = value.Title;
        form.Rating = value.Rating;
        form.IsPaidOnly = value.IsPaidOnly;
        form.Reference = value.Reference;
        form.Warning = value.Warning;
        form.HasMetadata = true;

        return await RenderAsync(form, cancellationToken);
    }

    private bool TryBuildRequest(DsProblemFormViewModel form, out DsProblemRequest request)
    {
        request = null!;

        if (form.Platform is null || string.IsNullOrWhiteSpace(form.ExternalId))
        {
            ModelState.AddModelError(
                nameof(form.Url),
                "Import the problem from its URL first, so the platform id is known.");
            return false;
        }

        if (form.DsSheetId is null)
        {
            ModelState.AddModelError(nameof(form.DsSheetId), "Pick the sheet this problem belongs to.");
            return false;
        }

        request = new DsProblemRequest(
            form.DsSheetId.Value,
            form.Platform.Value,
            form.ExternalId,
            form.Title ?? string.Empty,
            form.Difficulty,
            form.PointsOverride,
            form.Rating,
            form.IsPaidOnly);

        return true;
    }

    private async Task<IActionResult> RenderAsync(
        DsProblemFormViewModel form,
        CancellationToken cancellationToken)
    {
        var rows = await sheets.GetBoardListAsync(cancellationToken);

        form.Presets = await settings.GetPointsPresetsAsync(cancellationToken);

        form.Sheets = rows
            .Select(row => new DsSheetOption(row.Id, $"{row.MonthLabel} · {row.Title}"))
            .ToList();

        form.ReturnUrl ??= form.DsSheetId is { } sheetId
            ? Url.Action("Edit", "DsSheets", new { id = sheetId })
            : Url.Action("Index", "DsSheets");

        return View("Form", form);
    }

    private RedirectResult RedirectToSheet(Guid sheetId) =>
        Redirect(Url.Action("Edit", "DsSheets", new { id = sheetId }) + "#problems");
}