using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Ds;
using OscCommittee.Web.ViewModels.Ds;

namespace OscCommittee.Web.Controllers;

/// <summary>Member-facing DS browse: the overview and one sheet at a time (PLAN §7.2).</summary>
[Route("ds")]
[Authorize(Policy = "Member")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DsController(
    IDsSheetService sheets,
    IDsProblemService problems,
    IDsSyncService sync,
    IMarkdownService markdown,
    ITimeZoneService timeZone) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Data structures";

        var groups = await sheets.GetMemberGroupsAsync(cancellationToken);
        var local = timeZone.NowLocal();

        var mapped = groups.Select(group => new DsMonthGroupViewModel
        {
            Year = group.Year,
            Month = group.Month,
            Label = group.Label,
            IsCurrent = group.Year == local.Year && group.Month == local.Month,
            Sheets = group.Sheets.Select(ToCard).ToList()
        }).ToList();

        return View(new DsOverviewViewModel
        {
            MonthLabel = timeZone.CurrentMonthLabel(),
            ActiveSheet = mapped.SelectMany(group => group.Sheets).FirstOrDefault(card => card.IsActive),
            Groups = mapped
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Sheet(Guid id, CancellationToken cancellationToken)
    {
        var row = await sheets.GetVisibleAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        ViewData["Title"] = row.Title;

        var rows = await problems.GetForMemberAsync(row.Id, CurrentUserId, cancellationToken);
        var status = await sync.GetStatusAsync(CurrentUserId, cancellationToken);

        return View(new DsSheetPageViewModel
        {
            Id = row.Id,
            Title = row.Title,
            MonthLabel = row.MonthLabel,
            DueLabel = timeZone.FormatLocal(row.DueAtUtc, "d MMMM yyyy"),
            ShortDueLabel = timeZone.FormatLocal(row.DueAtUtc, "d MMM"),
            DueIso = row.DueAtUtc.ToString("O"),
            IsActive = row.IsActive,
            IsOverdue = row.DueAtUtc <= timeZone.UtcNow,
            DescriptionHtml = markdown.ToHtml(row.DescriptionMd),
            HasDescription = !string.IsNullOrWhiteSpace(row.DescriptionMd),
            ProblemCount = rows.Count,
            Sync = DsSyncButtonViewModel.From(status, Url.Action(nameof(Sheet), new { id = row.Id }) ?? "/ds"),
            Problems = rows.Select(ToCard).ToList()
        });
    }

    /// <summary>
    /// Runs the sync. JavaScript gets JSON; a plain form post gets a toast and a redirect.
    /// Status codes: 400 no handle, 409 already running, 503 all platforms failed.
    /// </summary>
    [HttpPost("sync")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await sync.SyncAsync(CurrentUserId, cancellationToken);
        var wantsJson = string.Equals(
            Request.Headers["X-Sync-Client"],
            "js",
            StringComparison.OrdinalIgnoreCase);

        if (wantsJson)
        {
            var statusCode = result.ErrorCode switch
            {
                "needs_handle" => StatusCodes.Status400BadRequest,
                "sync_in_progress" => StatusCodes.Status409Conflict,
                _ => result.Succeeded
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status503ServiceUnavailable
            };

            return StatusCode(statusCode, new
            {
                ok = result.Succeeded,
                message = result.Message,
                errorCode = result.ErrorCode,
                // One platform can fail while another succeeds; the reload must not paint that green.
                hasFailure = !result.Succeeded || result.Platforms.Any(platform => !platform.Succeeded),
                newSolves = result.NewSolves,
                points = result.Points,
                problems = result.Problems.Select(problem => new
                {
                    problem.Title,
                    problem.PlatformLabel,
                    problem.Points,
                    problem.IsLate
                }),
                platforms = result.Platforms.Select(platform => new
                {
                    platform = platform.Platform.ToString(),
                    platform.Succeeded,
                    platform.Skipped,
                    platform.ErrorCode,
                    platform.Message,
                    platform.NewSolves,
                    platform.Points
                })
            });
        }

        TempData[result.Succeeded ? "Toast.Success" : "Toast.Error"] = result.Message;

        return Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl!)
            : RedirectToAction(nameof(Index));
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

    private DsProblemCardViewModel ToCard(DsProblemRow problem) => new()
    {
        Id = problem.Id,
        Title = problem.Title,
        Url = problem.Url,
        PlatformLabel = problem.Platform == Platform.LeetCode ? "LeetCode" : "Codeforces",
        PlatformBadgeClass = problem.Platform == Platform.LeetCode ? "badge-platform-lc" : "badge-platform-cf",
        DifficultyLabel = problem.Difficulty.ToString(),
        DifficultyBadgeClass = problem.Difficulty switch
        {
            Difficulty.Easy => "badge-easy",
            Difficulty.Medium => "badge-medium",
            _ => "badge-hard"
        },
        Points = problem.Points,
        Rating = problem.Rating,
        IsPaidOnly = problem.IsPaidOnly,
        IsSolved = problem.Solve is not null,
        IsLate = problem.Solve?.IsLate ?? false,
        AwardedPoints = problem.Solve?.Points ?? 0,
        SolvedLabel = problem.Solve is null
            ? string.Empty
            : timeZone.FormatLocal(problem.Solve.SolvedAtUtc, "d MMM yyyy, HH:mm"),
        Language = problem.Solve?.Language,
        SubmissionUrl = problem.Solve?.PlatformUrl,
        IsManual = problem.Solve?.IsManual ?? false,
        ExternalId = problem.ExternalId
    };

    private DsSheetCardViewModel ToCard(DsSheetRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        MonthLabel = row.MonthLabel,
        DueLabel = timeZone.FormatLocal(row.DueAtUtc, "d MMM"),
        DueIso = row.DueAtUtc.ToString("O"),
        IsActive = row.IsActive,
        IsOverdue = row.DueAtUtc <= timeZone.UtcNow,
        ProblemCount = row.ProblemCount
    };
}