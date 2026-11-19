using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Ds;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Settings;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

[Route("board/ds/sheets")]
public sealed class DsSheetsController(
    IDsSheetService sheets,
    IDsProblemService problems,
    IMonthSheetService months,
    ISettingsService settings,
    ITimeZoneService timeZone) : BoardControllerBase
{
    private const string DueFormat = "yyyy-MM-dd";

    /// <summary>A paste is a convenience, not a bulk import; keep the wait bounded.</summary>
    private const int MaxLinksPerAdd = 20;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "DS sheets";

        // Opening the list guarantees the current month exists for the "new sheet" form.
        await months.GetOrCreateCurrentAsync(cancellationToken);

        var rows = await sheets.GetBoardListAsync(cancellationToken);
        var activeByMonth = rows
            .Where(row => row.IsActive)
            .ToDictionary(row => row.MonthSheetId, row => row.Title);

        var items = rows.Select(row => new DsSheetRowViewModel
        {
            Id = row.Id,
            MonthSheetId = row.MonthSheetId,
            Title = row.Title,
            MonthLabel = row.MonthLabel,
            DueLabel = timeZone.FormatLocal(row.DueAtUtc, "d MMM yyyy"),
            IsActive = row.IsActive,
            IsHidden = row.IsHidden,
            SortOrder = row.SortOrder,
            ProblemCount = row.ProblemCount,
            ActiveTitleInMonth = row.IsActive ? null : activeByMonth.GetValueOrDefault(row.MonthSheetId)
        }).ToList();

        return View(new DsSheetsViewModel
        {
            Sheets = items,
            TotalCount = items.Count,
            ActiveCount = items.Count(item => item.IsActive),
            HiddenCount = items.Count(item => item.IsHidden),
            CurrentMonthLabel = timeZone.CurrentMonthLabel()
        });
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New DS sheet";

        var current = await months.GetOrCreateCurrentAsync(cancellationToken);
        var rows = await sheets.GetBoardListAsync(cancellationToken);
        var nextSort = rows
            .Where(row => row.MonthSheetId == current.Id)
            .Select(row => row.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var form = new DsSheetFormViewModel
        {
            MonthSheetId = current.Id,
            DueLocal = ToLocalInput(timeZone.UtcNow.AddDays(7)),
            SortOrder = nextSort
        };

        return await RenderFormAsync(form, cancellationToken);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DsSheetFormViewModel form, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New DS sheet";

        if (!TryBuildRequest(form, out var request))
        {
            return await RenderFormAsync(form, cancellationToken);
        }

        if (form.IsHidden && form.IsActive)
        {
            ModelState.AddModelError(nameof(form.IsActive), DsSheetService.HiddenActiveMessage);
            return await RenderFormAsync(form, cancellationToken);
        }

        var urls = SplitLinks(form.NewProblemLinks);

        if (urls.Count > MaxLinksPerAdd)
        {
            form.AddProblemErrors = [$"Add up to {MaxLinksPerAdd} links at a time."];
            return await RenderFormAsync(form, cancellationToken);
        }

        if (form.IsActive && urls.Count == 0)
        {
            ModelState.AddModelError(
                nameof(form.IsActive),
                "Add at least one problem before making this sheet current.");
            return await RenderFormAsync(form, cancellationToken);
        }

        // Links are checked before anything is written, so a typo does not leave a half-made sheet.
        var previews = await PreviewLinksAsync(urls, cancellationToken);
        if (previews.Errors.Count > 0)
        {
            form.AddProblemErrors = previews.Errors;
            return await RenderFormAsync(form, cancellationToken);
        }

        // The service only creates empty drafts; activation follows once the problems are in.
        var created = await sheets.CreateAsync(
            request with { IsActive = false },
            CurrentUserId,
            cancellationToken);

        if (created.Failed)
        {
            ModelState.AddModelError(string.Empty, created.Message);
            return await RenderFormAsync(form, cancellationToken);
        }

        var sheetId = created.Value;

        var outcome = await CreateProblemsAsync(
            sheetId,
            previews.Items,
            form.NewProblemDifficulty,
            cancellationToken);

        var toast = new List<string> { created.Message };
        if (outcome.Added > 0)
        {
            toast.Add(outcome.Added == 1 ? "1 problem added." : $"{outcome.Added} problems added.");
        }

        var warning = outcome.Errors.Count > 0 ? string.Join(" ", outcome.Errors) : null;

        if (form.IsActive)
        {
            var activated = await sheets.ActivateAsync(sheetId, CurrentUserId, cancellationToken);
            if (activated.Succeeded)
            {
                toast.Add(activated.Message);
            }
            else
            {
                warning = JoinMessages(warning, activated.Message);
            }
        }

        if (outcome.WithoutDetails)
        {
            warning = JoinMessages(
                warning,
                "Some problems were saved without platform details. Check their titles and ratings.");
        }

        TempData["Toast.Success"] = string.Join(" ", toast);

        if (warning is not null)
        {
            TempData["Toast.Warning"] = warning;
        }

        // Problems that failed need the sheet page, where each one can be retried.
        return outcome.Errors.Count > 0
            ? Redirect(Url.Action(nameof(Edit), new { id = sheetId }) + "#problems")
            : RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "DS sheet";

        var row = await sheets.FindAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        return await RenderFormAsync(ToForm(row), cancellationToken);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        DsSheetFormViewModel form,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Edit DS sheet";
        form = CloneWithId(form, id);

        if (await sheets.FindAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        if (!TryBuildRequest(form, out var request))
        {
            return await RenderFormAsync(form, cancellationToken);
        }

        var result = await sheets.UpdateAsync(id, request, CurrentUserId, cancellationToken);
        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return await RenderFormAsync(form, cancellationToken);
        }

        Toast(result);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Adds problems from links pasted on the sheet page. LeetCode and Codeforces are the only
    /// accepted sources; rejected links come back under the add box with their own message.
    /// </summary>
    [HttpPost("{id:guid}/problems")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProblems(
        Guid id,
        string? links,
        Difficulty difficulty,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "DS sheet";

        var row = await sheets.FindAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        var form = ToForm(row);
        form.NewProblemLinks = links;
        form.NewProblemDifficulty = difficulty;

        var urls = SplitLinks(links);

        if (urls.Count == 0)
        {
            form.AddProblemErrors = ["Paste at least one Codeforces or LeetCode link."];
            return await RenderFormAsync(form, cancellationToken);
        }

        if (urls.Count > MaxLinksPerAdd)
        {
            form.AddProblemErrors = [$"Add up to {MaxLinksPerAdd} links at a time."];
            return await RenderFormAsync(form, cancellationToken);
        }

        var batch = await PreviewLinksAsync(urls, cancellationToken);
        var outcome = await CreateProblemsAsync(
            id,
            batch.Items,
            difficulty,
            cancellationToken);

        var errors = batch.Errors.Concat(outcome.Errors).ToList();

        if (outcome.Added > 0)
        {
            TempData["Toast.Success"] = outcome.Added == 1
                ? "1 problem added to the sheet."
                : $"{outcome.Added} problems added to the sheet.";
        }

        if (outcome.WithoutDetails)
        {
            TempData["Toast.Warning"] =
                "Some problems were saved without platform details. Check their titles and ratings.";
        }

        if (errors.Count > 0)
        {
            form.AddProblemErrors = errors;
            form.NewProblemLinks = string.Join(
                Environment.NewLine,
                batch.Rejected.Concat(outcome.Rejected).Distinct(StringComparer.OrdinalIgnoreCase));
            return await RenderFormAsync(form, cancellationToken);
        }

        return Redirect(Url.Action("Edit", "DsSheets", new { id }) + "#problems");
    }

    [HttpPost("{id:guid}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        Toast(await sheets.ActivateAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        Toast(await sheets.DeactivateAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/hide")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hide(Guid id, bool hidden, CancellationToken cancellationToken)
    {
        Toast(await sheets.SetHiddenAsync(id, hidden, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> RenderFormAsync(
        DsSheetFormViewModel form,
        CancellationToken cancellationToken)
    {
        await months.GetOrCreateCurrentAsync(cancellationToken);

        var local = timeZone.NowLocal();
        var listed = await months.GetListAsync(cancellationToken);

        form.Months = listed
            .Select(month => new DsMonthOption(
                month.Id,
                month.Label,
                month.Year == local.Year && month.Month == local.Month))
            .ToList();
        form.TimeZoneId = timeZone.TimeZone.Id;
        form.Presets = await settings.GetPointsPresetsAsync(cancellationToken);

        // Problems are managed from the same page once the sheet exists.
        if (form.Id is { } sheetId)
        {
            var rows = await problems.GetForSheetAsync(sheetId, cancellationToken);
            form.Problems = DsProblemMap.ToRows(rows, timeZone);
        }

        return View("Form", form);
    }

    /// <summary>Parses the local deadline and fills any other missing values into ModelState.</summary>
    private bool TryBuildRequest(DsSheetFormViewModel form, out DsSheetRequest request)
    {
        request = null!;
        var valid = true;

        if (!TryParseDue(form.DueLocal, out var due))
        {
            ModelState.AddModelError(nameof(form.DueLocal), "Pick a valid deadline.");
            valid = false;
        }

        if (form.MonthSheetId is null)
        {
            ModelState.AddModelError(nameof(form.MonthSheetId), "Pick the month this sheet belongs to.");
            valid = false;
        }

        if (!valid)
        {
            return false;
        }

        request = new DsSheetRequest(
            form.Title ?? string.Empty,
            form.DescriptionMd,
            form.MonthSheetId!.Value,
            due,
            form.IsHidden,
            form.IsActive,
            form.SortOrder);

        return true;
    }

    /// <summary>
    /// The form posts a local calendar date. The deadline is the last instant of that day in the
    /// configured time zone, so "25 Oct" stays open through 23:59 and closes on the 26th.
    /// </summary>
    private bool TryParseDue(string? raw, out DateTimeOffset due)
    {
        due = default;

        if (string.IsNullOrWhiteSpace(raw)
            || !DateTime.TryParseExact(
                raw.Trim(),
                DueFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return false;
        }

        var local = date.Date.AddDays(1).AddTicks(-1);
        if (timeZone.TimeZone.IsInvalidTime(local))
        {
            return false;
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            timeZone.TimeZone);

        due = new DateTimeOffset(utc, TimeSpan.Zero);
        return true;
    }

    private string ToLocalInput(DateTimeOffset utc) =>
        timeZone.ToLocal(utc).ToString(DueFormat, CultureInfo.InvariantCulture);

    private DsSheetFormViewModel ToForm(DsSheetRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        DescriptionMd = row.DescriptionMd,
        MonthSheetId = row.MonthSheetId,
        DueLocal = ToLocalInput(row.DueAtUtc),
        SortOrder = row.SortOrder,
        IsHidden = row.IsHidden,
        IsActive = row.IsActive
    };

    /// <summary>One link per line, but a pasted column of links is just as acceptable.</summary>
    private static IReadOnlyList<string> SplitLinks(string? links) =>
        string.IsNullOrWhiteSpace(links)
            ? []
            : links
                .Split(
                    ['\r', '\n', ',', ' ', '\t'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>
    /// Resolves every pasted link before anything is written. Rejections come back per link so the
    /// form can show them without losing the pasted text.
    /// </summary>
    private async Task<LinkPreviewBatch> PreviewLinksAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        var items = new List<PreviewedLink>();
        var rejected = new List<string>();
        var errors = new List<string>();

        foreach (var url in urls)
        {
            var preview = await problems.PreviewAsync(url, cancellationToken);

            if (preview.Failed || preview.Value is null)
            {
                errors.Add($"{url} — {preview.Message}");
                rejected.Add(url);
                continue;
            }

            items.Add(new PreviewedLink(url, preview.Value));
        }

        return new LinkPreviewBatch(items, rejected, errors);
    }

    /// <summary>Writes the already previewed problems, reporting per-link failures.</summary>
    private async Task<ProblemAddOutcome> CreateProblemsAsync(
        Guid sheetId,
        IReadOnlyList<PreviewedLink> links,
        Difficulty difficulty,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var withoutDetails = false;
        var errors = new List<string>();
        var rejected = new List<string>();

        foreach (var link in links)
        {
            var value = link.Preview;

            var result = await problems.CreateAsync(
                new DsProblemRequest(
                    sheetId,
                    value.Platform,
                    value.ExternalId,
                    value.Title,
                    difficulty,
                    null,
                    value.Rating,
                    value.IsPaidOnly),
                CurrentUserId,
                cancellationToken);

            if (result.Failed)
            {
                errors.Add(result.Message);
                rejected.Add(link.Url);
                continue;
            }

            added++;
            withoutDetails |= !string.IsNullOrWhiteSpace(value.Warning);
        }

        return new ProblemAddOutcome(added, withoutDetails, errors, rejected);
    }

    private static string? JoinMessages(string? first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first} {second}";

    private sealed record PreviewedLink(string Url, DsProblemPreview Preview);

    private sealed record LinkPreviewBatch(
        IReadOnlyList<PreviewedLink> Items,
        IReadOnlyList<string> Rejected,
        IReadOnlyList<string> Errors);

    private sealed record ProblemAddOutcome(
        int Added,
        bool WithoutDetails,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Rejected);

    private static DsSheetFormViewModel CloneWithId(DsSheetFormViewModel form, Guid id) => new()
    {
        Id = id,
        Title = form.Title,
        DescriptionMd = form.DescriptionMd,
        MonthSheetId = form.MonthSheetId,
        DueLocal = form.DueLocal,
        SortOrder = form.SortOrder,
        IsHidden = form.IsHidden,
        IsActive = form.IsActive
    };
}