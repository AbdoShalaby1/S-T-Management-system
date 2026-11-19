using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;
using OscCommittee.Web.ViewModels.Board;

namespace OscCommittee.Web.Controllers.Board;

/// <summary>
/// Board review of one stored C# attempt (PLAN §8.7): read the files, correct the score, and
/// retry grading. Every write goes through <see cref="IBoardSubmissionService"/>.
/// </summary>
[Route("board/submissions")]
public sealed class SubmissionsController(
    IBoardSubmissionService submissions,
    ISubmissionFileService files,
    IReferenceFileService references,
    IMarkdownService markdown,
    ITimeZoneService timeZone) : BoardControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Review(
        Guid id,
        string? file = null,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Review submission";

        var detail = await submissions.FindAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        var model = await BuildViewModelAsync(detail, file, reference, cancellationToken);
        return View(model);
    }

    [HttpPost("{id:guid}/score")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Score(
        Guid id,
        BoardSubmissionScoreForm form,
        string? file = null,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Review submission";

        var detail = await submissions.FindAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            var invalid = await BuildViewModelAsync(detail, file, reference, cancellationToken);
            return View("Review", invalid);
        }

        var result = await submissions.SaveScoreAsync(
            id,
            new BoardScoreEdit(form.Score, form.Summary, form.RowVersion),
            CurrentUserId,
            cancellationToken);

        Toast(result);

        // A conflict or a missing attempt needs a fresh read, not a stale form.
        if (result.Failed)
        {
            return RedirectToAction(nameof(Review), new { id });
        }

        return RedirectToAction(nameof(Review), new { id, file, reference });
    }

    [HttpPost("{id:guid}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        Toast(await submissions.RetryAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var archive = await files.BuildArchiveAsync(id, cancellationToken);
        if (archive.Value is not { } bundle)
        {
            Toast(archive);
            return RedirectToAction(nameof(Review), new { id });
        }

        return File(bundle.Bytes, "application/zip", bundle.FileName);
    }

    private async Task<BoardSubmissionReviewViewModel> BuildViewModelAsync(
        BoardSubmissionDetail detail,
        string? file,
        string? reference,
        CancellationToken cancellationToken)
    {
        var content = markdown.ToContent(detail.StatementMd);
        var storedFiles = await files.ReadAsync(detail.Id, file, cancellationToken);
        var referenceView = detail.ReferenceFileCount > 0
            ? await references.ReadAsync(detail.ReferenceFolderPath, reference, cancellationToken)
            : null;

        return new BoardSubmissionReviewViewModel
        {
            Id = detail.Id,
            TaskId = detail.TaskId,
            TaskTitle = detail.TaskTitle,
            MonthLabel = detail.MonthLabel,
            MemberName = detail.MemberName,
            MemberEmail = detail.MemberEmail,
            MemberInitials = UserStatusView.Initials(detail.MemberName),
            AttemptNumber = detail.AttemptNo,
            SubmittedLabel = timeZone.FormatLocal(detail.SubmittedAtUtc, "d MMM yyyy · HH:mm"),
            GradedLabel = detail.GradedAtUtc is { } graded
                ? timeZone.FormatLocal(graded, "d MMM yyyy · HH:mm")
                : null,
            IsLate = detail.IsLate,
            Status = detail.Status,
            FileCount = detail.FileCount,
            IgnoredFileCount = detail.IgnoredFileCount,
            TotalSizeLabel = MarkdownBundleService.FormatBytes(detail.TotalBytes),
            BundleChars = detail.BundleChars,
            Score = detail.Score,
            AiScore = detail.AiScore,
            IsManualScore = detail.IsManualScore,
            Summary = detail.Summary,
            Verdict = detail.Verdict,
            ErrorMessage = detail.ErrorMessage,
            GradingModel = detail.GradingModel,
            PromptVersion = detail.PromptVersion,
            PromptHashShort = detail.PromptHash is { Length: > 8 }
                ? detail.PromptHash[..8]
                : detail.PromptHash,
            InputChars = detail.InputChars,
            InputTokens = detail.InputTokens,
            OutputTokens = detail.OutputTokens,
            RetryCount = detail.RetryCount,
            RowVersion = detail.RowVersion,
            GradingConfigured = detail.GradingConfigured,
            StatementHtml = content.Html,
            HasStatement = !string.IsNullOrWhiteSpace(detail.StatementMd),
            HasReference = detail.ReferenceFileCount > 0,
            ReferenceFolderPath = detail.ReferenceFolderPath,
            ReferenceFiles = referenceView,
            Feedback = detail.Feedback,
            Files = storedFiles?.Files ?? [],
            SelectedPath = storedFiles?.SelectedPath,
            SelectedContent = storedFiles?.Content,
            SelectedLanguage = storedFiles?.Language ?? "csharp",
            SelectedTruncated = storedFiles?.Truncated ?? false,
            FileError = storedFiles?.Error
        };
    }
}