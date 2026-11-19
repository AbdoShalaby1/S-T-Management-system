using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Accounts;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;
using OscCommittee.Web.ViewModels.Tasks;
using SubmissionState = OscCommittee.Web.Domain.Enums.SubmissionStatus;

namespace OscCommittee.Web.Controllers;

/// <summary>Member-facing C# tasks: the list, one task page, folder upload and submissions.</summary>
[Route("tasks")]
[Authorize(Policy = "Member")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TasksController(
    ICSharpTaskService tasks,
    ICSharpSubmissionService submissions,
    IReferenceFileService references,
    IStarterZipService starters,
    IMarkdownService markdown,
    ITimeZoneService timeZone,
    IOptions<UploadsOptions> uploads,
    IOptions<AiOptions> ai) : Controller
{
    private const long MaxRequestBytes = 64L * 1024 * 1024;
    private const string BoardCannotSubmit = "Board accounts cannot submit tasks. Grade attempts from the board area.";

    private Guid CurrentUserId
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id)
                ? id
                : throw new InvalidOperationException("The member policy ran without a user id claim.");
        }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "C# tasks";

        var rows = await tasks.GetMemberListAsync(cancellationToken);
        var summaries = (await submissions.GetTaskSummariesAsync(CurrentUserId, cancellationToken))
            .ToDictionary(summary => summary.TaskId);
        var local = timeZone.NowLocal();

        var groups = rows
            .GroupBy(row => new { row.Year, row.Month })
            .OrderByDescending(group => group.Key.Year)
            .ThenByDescending(group => group.Key.Month)
            .Select(group => new TaskMonthGroupViewModel
            {
                Year = group.Key.Year,
                Month = group.Key.Month,
                Label = group.First().MonthLabel,
                IsCurrent = group.Key.Year == local.Year && group.Key.Month == local.Month,
                Tasks = group
                    .OrderByDescending(row => row.IsActive)
                    .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(row => ToRow(row, summaries.GetValueOrDefault(row.Id)))
                    .ToList()
            })
            .ToList();

        return View(new TasksViewModel
        {
            MonthLabel = timeZone.CurrentMonthLabel(),
            Active = groups.SelectMany(group => group.Tasks).FirstOrDefault(row => row.IsActive),
            Groups = groups
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(
        Guid id,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetVisibleAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        ViewData["Title"] = task.Title;

        var content = markdown.ToContent(task.DescriptionMd);
        var hasReference = task.ReferenceFileCount > 0;
        // No toggle: the reference is shared automatically once the deadline has passed.
        var showReference = hasReference && task.DueAtUtc <= timeZone.UtcNow;
        var referenceView = showReference
            ? await references.ReadAsync(task.ReferenceFolderPath, reference, cancellationToken)
            : null;
        var attempts = await GetAttemptViewModelsAsync(task.Id, cancellationToken);
        var best = attempts.FirstOrDefault(attempt => attempt.IsBest);
        var latest = attempts.FirstOrDefault();

        return View(new TaskPageViewModel
        {
            Id = task.Id,
            Title = task.Title,
            MonthLabel = task.MonthLabel,
            DueLabel = timeZone.FormatLocal(task.DueAtUtc, "d MMMM yyyy"),
            DueIso = task.DueAtUtc.ToString("O"),
            IsActive = task.IsActive,
            IsOverdue = task.DueAtUtc <= timeZone.UtcNow,
            StatementHtml = content.Html,
            HasStatement = !string.IsNullOrWhiteSpace(task.DescriptionMd),
            MaxPoints = ICSharpTaskService.MaxPoints,
            MaxFiles = uploads.Value.MaxFiles,
            MaxTotalMb = uploads.Value.MaxTotalMb,
            MaxFileMb = uploads.Value.MaxFileMb,
            HasStarterZip = !string.IsNullOrWhiteSpace(task.StarterZipPath),
            StarterZipFileName = task.StarterZipFileName,
            StarterZipSizeBytes = task.StarterZipSizeBytes,
            HasReference = hasReference,
            ShowReference = showReference,
            ReferenceFiles = referenceView,
            Attempts = attempts,
            BestScore = best?.Score,
            BestPoints = best?.Points,
            LatestStatus = latest?.Status,
            LatestAttemptId = latest?.Id,
            CanSubmit = latest is null || latest.Status == SubmissionState.TryAgain
        });
    }

    /// <summary>The starting-code zip for a task the member can see.</summary>
    [HttpGet("{id:guid}/starter/download")]
    public async Task<IActionResult> DownloadStarter(Guid id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetVisibleAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        var starter = starters.Read(task.StarterZipPath, task.StarterZipFileName);
        if (starter is null)
        {
            TempData["Toast.Error"] = "The starting-code zip is missing. Ask the board to upload it again.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        return PhysicalFile(starter.FullPath, "application/zip", starter.FileName);
    }

    /// <summary>The reference folder as a zip, only once the task deadline has passed.</summary>
    [HttpGet("{id:guid}/reference/download")]
    public async Task<IActionResult> DownloadReference(Guid id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetVisibleAsync(id, cancellationToken);
        if (task is null || task.ReferenceFileCount == 0 || task.ReferenceFolderPath is null)
        {
            return NotFound();
        }

        if (task.DueAtUtc > timeZone.UtcNow)
        {
            return NotFound();
        }

        var archive = await references.BuildArchiveAsync(task.ReferenceFolderPath, task.Title, cancellationToken);
        if (archive.Value is not { } bundle)
        {
            TempData["Toast.Error"] = archive.Message;
            return RedirectToAction(nameof(Detail), new { id });
        }

        return File(bundle.Bytes, "application/zip", bundle.FileName);
    }

    /// <summary>
    /// The upload form now lives on the task page. Old links land on the task.
    /// </summary>
    [HttpGet("{id:guid}/submit")]
    public IActionResult Submit(Guid id) => RedirectToAction(nameof(Detail), new { id });

    [HttpPost("{id:guid}/submit")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Submit(
        Guid id,
        List<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        var task = await tasks.GetVisibleAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        if (User.IsInRole(Roles.Board))
        {
            TempData["Toast.Error"] = BoardCannotSubmit;
            return RedirectToAction(nameof(Detail), new { id });
        }

        // An empty <input type="file"> posts one blank part in some browsers.
        var selected = (files ?? [])
            .Where(file => !string.IsNullOrWhiteSpace(file.FileName))
            .ToList();

        if (selected.Count == 0)
        {
            TempData["Toast.Error"] = UploadCopy.NoFiles;
            return RedirectToAction(nameof(Detail), new { id });
        }

        var inputs = selected
            .Select(file => new SubmissionInputFile(
                file.FileName,
                file.Length,
                _ => Task.FromResult(file.OpenReadStream())))
            .ToList();

        var result = await submissions.SubmitAsync(id, CurrentUserId, inputs, cancellationToken);
        if (result.Failed)
        {
            TempData["Toast.Error"] = result.Message;

            return result.ErrorCode == "not_found"
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(Detail), new { id });
        }

        var receipt = result.Value!;
        var message = $"Attempt {receipt.AttemptNo} saved — {receipt.KeptFiles} " +
                      (receipt.KeptFiles == 1 ? "file" : "files");

        if (receipt.IgnoredFiles > 0)
        {
            message += $", {receipt.IgnoredFiles} ignored";
        }

        message += receipt.IsLate
            ? ". This one is late: it is graded normally but worth 0 points."
            : ".";

        TempData["Toast.Success"] = message;

        if (receipt.DuplicateFiles > 0)
        {
            TempData["Toast.Warning"] = receipt.DuplicateFiles == 1
                ? "One path appeared twice. The last copy was kept."
                : $"{receipt.DuplicateFiles} paths appeared more than once. The last copy of each was kept.";
        }

        return RedirectToAction(nameof(Submission), new { id = receipt.SubmissionId });
    }

    [HttpGet("submissions/{id:guid}")]
    public async Task<IActionResult> Submission(Guid id, CancellationToken cancellationToken)
    {
        var submission = await submissions.FindForMemberAsync(id, CurrentUserId, cancellationToken);
        if (submission is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Submission";

        return View(new SubmissionPageViewModel
        {
            Id = submission.Id,
            TaskId = submission.TaskId,
            TaskTitle = submission.TaskTitle,
            MonthLabel = submission.MonthLabel,
            AttemptNumber = submission.AttemptNo,
            SubmittedLabel = timeZone.FormatLocal(submission.SubmittedAtUtc, "d MMM yyyy, HH:mm"),
            IsLate = submission.IsLate,
            Status = submission.Status,
            Score = submission.Score,
            Points = submission.Points,
            Summary = submission.Summary,
            Verdict = submission.Verdict,
            ErrorMessage = submission.ErrorMessage,
            GradingConfigured = ai.Value.IsConfigured,
            Feedback = submission.Feedback
        });
    }

    /// <summary>Polled by <c>poll.js</c>; Part 10 drives the status changes.</summary>
    [HttpGet("submissions/{id:guid}/status")]
    public async Task<IActionResult> SubmissionStatus(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await submissions.GetStatusAsync(id, CurrentUserId, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            id = snapshot.Id,
            status = snapshot.Status.ToString(),
            statusLabel = SubmissionStatusView.Label(snapshot.Status),
            score = snapshot.Score,
            points = snapshot.Points,
            isLate = snapshot.IsLate,
            gradingConfigured = snapshot.GradingConfigured,
            errorMessage = snapshot.ErrorMessage,
            gradedAtUtc = snapshot.GradedAtUtc
        });
    }

    private async Task<IReadOnlyList<TaskAttemptRowViewModel>> GetAttemptViewModelsAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var rows = await submissions.GetAttemptsAsync(taskId, CurrentUserId, cancellationToken);

        return rows
            .Select(row => new TaskAttemptRowViewModel
            {
                Id = row.Id,
                AttemptNumber = row.AttemptNo,
                SubmittedLabel = timeZone.FormatLocal(row.SubmittedAtUtc, "d MMM yyyy, HH:mm"),
                SubmittedShortLabel = timeZone.FormatLocal(row.SubmittedAtUtc, "d MMM, HH:mm"),
                FileCount = row.FileCount,
                IsLate = row.IsLate,
                Status = row.Status,
                Score = row.Score,
                AiScore = row.AiScore,
                Points = row.Points,
                IsBest = row.IsBest,
                HasFeedback = !string.IsNullOrWhiteSpace(row.Summary),
                Summary = row.Summary
            })
            .ToList();
    }

    private TaskRowViewModel ToRow(CSharpTaskRow row, TaskAttemptSummary? summary) => new()
    {
        Id = row.Id,
        Title = row.Title,
        MonthLabel = row.MonthLabel,
        DueLabel = timeZone.FormatLocal(row.DueAtUtc, "d MMM"),
        DueIso = row.DueAtUtc.ToString("O"),
        IsActive = row.IsActive,
        IsOverdue = row.DueAtUtc <= timeZone.UtcNow,
        MaxPoints = ICSharpTaskService.MaxPoints,
        AttemptCount = summary?.AttemptCount ?? 0,
        BestScore = summary?.BestScore,
        LatestStatus = summary?.LatestStatus
    };
}