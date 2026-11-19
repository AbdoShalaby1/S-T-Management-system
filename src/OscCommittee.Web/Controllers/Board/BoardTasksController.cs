using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;
using OscCommittee.Web.ViewModels.Board;
using OscCommittee.Web.ViewModels.Tasks;

namespace OscCommittee.Web.Controllers.Board;

/// <summary>Board-side C# task management (PLAN §8.6): statement, reference, visibility.</summary>
[Route("board/tasks")]
public sealed class BoardTasksController(
    ICSharpTaskService tasks,
    ICSharpSubmissionService submissions,
    IReferenceFileService references,
    IStarterZipService starters,
    IMonthSheetService months,
    IMarkdownService markdown,
    ITimeZoneService timeZone) : BoardControllerBase
{
    private const string DueFormat = "yyyy-MM-dd";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "C# tasks";

        // Opening the list guarantees the current month exists for the "new task" form.
        await months.GetOrCreateCurrentAsync(cancellationToken);

        var rows = await tasks.GetBoardListAsync(cancellationToken);
        var activeByMonth = rows
            .Where(row => row.IsActive)
            .ToDictionary(row => row.MonthSheetId, row => row.Title);

        var items = rows.Select(row => new CSharpTaskRowViewModel
        {
            Id = row.Id,
            MonthSheetId = row.MonthSheetId,
            Title = row.Title,
            MonthLabel = row.MonthLabel,
            DueLabel = timeZone.FormatLocal(row.DueAtUtc, "d MMM yyyy"),
            IsActive = row.IsActive,
            IsHidden = row.IsHidden,
            HasReference = row.HasReference,
            ReferenceFileCount = row.ReferenceFileCount,
            ActiveTitleInMonth = row.IsActive ? null : activeByMonth.GetValueOrDefault(row.MonthSheetId)
        }).ToList();

        return View(new CSharpTasksViewModel
        {
            Tasks = items,
            TotalCount = items.Count,
            ActiveCount = items.Count(item => item.IsActive),
            HiddenCount = items.Count(item => item.IsHidden),
            CurrentMonthLabel = timeZone.CurrentMonthLabel()
        });
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New C# task";

        var current = await months.GetOrCreateCurrentAsync(cancellationToken);

        var form = new CSharpTaskFormViewModel
        {
            MonthSheetId = current.Id,
            DueLocal = ToLocalInput(timeZone.UtcNow.AddDays(7))
        };

        return await RenderFormAsync(form, cancellationToken);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(64L * 1024 * 1024)]
    public async Task<IActionResult> Create(
        CSharpTaskFormViewModel form,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New C# task";

        if (!await TryBuildRequestAsync(form, cancellationToken))
        {
            return await RenderFormAsync(form, cancellationToken);
        }

        var result = await tasks.CreateAsync(form.Request!, CurrentUserId, cancellationToken);
        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return await RenderFormAsync(form, cancellationToken);
        }

        Toast(Result.Ok(result.Message));
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(
        Guid id,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Task detail";

        var task = await tasks.FindAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        var attempts = await submissions.GetBoardAttemptsAsync(id, cancellationToken);
        var referenceView = task.ReferenceFileCount > 0
            ? await references.ReadAsync(task.ReferenceFolderPath, reference, cancellationToken)
            : null;

        return View(new CSharpTaskDetailViewModel
        {
            Id = task.Id,
            Title = task.Title,
            MonthLabel = task.MonthLabel,
            DueLabel = timeZone.FormatLocal(task.DueAtUtc, "d MMM yyyy"),
            DueAtUtc = task.DueAtUtc,
            IsActive = task.IsActive,
            IsHidden = task.IsHidden,
            HasStarterZip = !string.IsNullOrWhiteSpace(task.StarterZipPath),
            StarterZipFileName = task.StarterZipFileName,
            StarterZipSizeBytes = task.StarterZipSizeBytes,
            HasReference = task.ReferenceFileCount > 0,
            ReferenceFolderPath = task.ReferenceFolderPath,
            ReferenceFiles = referenceView,
            StatementHtml = markdown.ToHtml(task.DescriptionMd),
            HasStatement = !string.IsNullOrWhiteSpace(task.DescriptionMd),
            Attempts = attempts
                .Select(row => new BoardSubmissionRowViewModel
                {
                    Id = row.Id,
                    MemberName = row.MemberName,
                    AttemptNumber = row.AttemptNo,
                    SubmittedLabel = timeZone.FormatLocal(row.SubmittedAtUtc, "d MMM yyyy · HH:mm"),
                    FileCount = row.FileCount,
                    IsLate = row.IsLate,
                    Status = row.Status,
                    Score = row.Score,
                    Points = row.Points
                })
                .ToList()
        });
    }

    [HttpGet("{id:guid}/reference/download")]
    public async Task<IActionResult> DownloadReference(Guid id, CancellationToken cancellationToken)
    {
        var task = await tasks.FindAsync(id, cancellationToken);
        if (task is null || task.ReferenceFileCount == 0 || task.ReferenceFolderPath is null)
        {
            return NotFound();
        }

        var archive = await references.BuildArchiveAsync(task.ReferenceFolderPath, task.Title, cancellationToken);
        if (archive.Value is not { } bundle)
        {
            Toast(archive);
            return RedirectToAction(nameof(Detail), new { id });
        }

        return File(bundle.Bytes, "application/zip", bundle.FileName);
    }

    /// <summary>The starting-code zip, available to the board even before members can see it.</summary>
    [HttpGet("{id:guid}/starter/download")]
    public async Task<IActionResult> DownloadStarter(Guid id, CancellationToken cancellationToken)
    {
        var task = await tasks.FindAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        var starter = starters.Read(task.StarterZipPath, task.StarterZipFileName);
        if (starter is null)
        {
            Toast(Result.Fail("missing_starter", "The starting-code zip is missing. Upload it again from the edit page."));
            return RedirectToAction(nameof(Detail), new { id });
        }

        return PhysicalFile(starter.FullPath, "application/zip", starter.FileName);
    }

    [HttpGet("{id:guid}/submissions")]
    public async Task<IActionResult> Submissions(
        Guid id,
        string? sort = null,
        string? status = null,
        string? q = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Task submissions";

        var task = await tasks.FindAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        var rows = await submissions.GetBoardAttemptsAsync(id, cancellationToken);
        var normalizedStatus = NormalizeStatus(status);
        var query = q?.Trim();
        var normalizedSort = sort switch
        {
            "member" => "member",
            "score" => "score",
            "attempt" => "attempt",
            _ => "submitted"
        };

        IEnumerable<BoardSubmissionRow> filtered = rows;

        if (normalizedStatus != "all")
        {
            filtered = normalizedStatus switch
            {
                "graded" => filtered.Where(row => row.Status == SubmissionStatus.Graded),
                "pending" => filtered.Where(row =>
                    row.Status is SubmissionStatus.Uploaded or SubmissionStatus.Grading or SubmissionStatus.AwaitingReview),
                "failed" => filtered.Where(row => row.Status == SubmissionStatus.Failed),
                "late" => filtered.Where(row => row.IsLate),
                _ => filtered
            };
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(row =>
                row.MemberName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = normalizedSort switch
        {
            "member" => filtered
                .OrderBy(row => row.MemberName)
                .ThenBy(row => row.AttemptNo),
            "score" => filtered
                .OrderByDescending(row => row.Points ?? -1)
                .ThenByDescending(row => row.SubmittedAtUtc),
            "attempt" => filtered
                .OrderByDescending(row => row.AttemptNo)
                .ThenBy(row => row.MemberName),
            _ => filtered.OrderByDescending(row => row.SubmittedAtUtc)
        };

        return View(new CSharpTaskSubmissionsViewModel
        {
            TaskId = task.Id,
            TaskTitle = task.Title,
            MonthLabel = task.MonthLabel,
            IsHidden = task.IsHidden,
            Sort = normalizedSort,
            StatusFilter = normalizedStatus,
            Query = query,
            TotalCount = rows.Count,
            GradedCount = rows.Count(row => row.Status == SubmissionStatus.Graded),
            PendingCount = rows.Count(row =>
                row.Status is SubmissionStatus.Uploaded or SubmissionStatus.Grading or SubmissionStatus.AwaitingReview),
            FailedCount = rows.Count(row => row.Status == SubmissionStatus.Failed),
            LateCount = rows.Count(row => row.IsLate),
            MemberCount = rows.Select(row => row.UserId).Distinct().Count(),
            Attempts = ordered
                .Select(row => new BoardSubmissionAttemptViewModel
                {
                    Id = row.Id,
                    UserId = row.UserId,
                    MemberName = row.MemberName,
                    Initials = UserStatusView.Initials(row.MemberName),
                    AttemptNumber = row.AttemptNo,
                    SubmittedLabel = timeZone.FormatLocal(row.SubmittedAtUtc, "d MMM yyyy · HH:mm"),
                    FileCount = row.FileCount,
                    IsLate = row.IsLate,
                    Status = row.Status,
                    Score = row.Score,
                    Points = row.Points,
                    IsBest = row.IsBest
                })
                .ToList()
        });
    }

    private static string NormalizeStatus(string? status) => status switch
    {
        "graded" => "graded",
        "pending" => "pending",
        "failed" => "failed",
        "late" => "late",
        _ => "all"
    };

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Edit C# task";

        var task = await tasks.FindAsync(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        return await RenderFormAsync(new CSharpTaskFormViewModel
        {
            Id = task.Id,
            Title = task.Title,
            DescriptionMd = task.DescriptionMd,
            MonthSheetId = task.MonthSheetId,
            DueLocal = ToLocalInput(task.DueAtUtc),
            ExistingStarterZipPath = task.StarterZipPath,
            ExistingStarterZipFileName = task.StarterZipFileName,
            ExistingStarterZipSizeBytes = task.StarterZipSizeBytes,
            ExistingReferenceFolderPath = task.ReferenceFolderPath,
            ExistingReferenceFileCount = task.ReferenceFileCount,
            IsHidden = task.IsHidden,
            IsActive = task.IsActive
        }, cancellationToken);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(64L * 1024 * 1024)]
    public async Task<IActionResult> Edit(
        Guid id,
        CSharpTaskFormViewModel form,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Edit C# task";
        form = CloneWithId(form, id);

        var existing = await tasks.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (!await TryBuildRequestAsync(form, cancellationToken))
        {
            return await RenderFormAsync(form, cancellationToken);
        }

        var result = await tasks.UpdateAsync(id, form.Request!, CurrentUserId, cancellationToken);
        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return await RenderFormAsync(form, cancellationToken);
        }

        // Only after the row is saved: a failed save must not leave the task pointing at a
        // deleted folder. A replaced or cleared reference is removed from disk.
        if (!string.IsNullOrWhiteSpace(existing.ReferenceFolderPath)
            && !string.Equals(
                existing.ReferenceFolderPath,
                form.Request!.ReferenceFolderPath,
                StringComparison.Ordinal))
        {
            references.Delete(existing.ReferenceFolderPath);
        }

        if (!string.IsNullOrWhiteSpace(existing.StarterZipPath)
            && !string.Equals(
                existing.StarterZipPath,
                form.Request!.StarterZipPath,
                StringComparison.Ordinal))
        {
            starters.Delete(existing.StarterZipPath);
        }

        Toast(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        Toast(await tasks.ActivateAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        Toast(await tasks.DeactivateAsync(id, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/hide")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hide(Guid id, bool hidden, CancellationToken cancellationToken)
    {
        Toast(await tasks.SetHiddenAsync(id, hidden, CurrentUserId, cancellationToken));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Live Markdown preview for the editor tabs. JSON only.</summary>
    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public IActionResult Preview([FromForm(Name = "markdown")] string? text)
    {
        var content = markdown.ToContent(text);

        return Ok(new
        {
            ok = true,
            html = content.Html,
            headings = content.Headings.Select(heading => new
            {
                heading.Level,
                heading.Text,
                heading.Id
            })
        });
    }

    private async Task<IActionResult> RenderFormAsync(
        CSharpTaskFormViewModel form,
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

        return View("Form", form);
    }

    /// <summary>
    /// Parses the deadline and stores an uploaded starting-code zip or reference folder when one was
    /// sent. When no new upload arrives the existing file/folder is kept unless the matching clear
    /// flag is set.
    /// </summary>
    private async Task<bool> TryBuildRequestAsync(
        CSharpTaskFormViewModel form,
        CancellationToken cancellationToken)
    {
        var valid = true;

        if (!TryParseDue(form.DueLocal, out var due))
        {
            ModelState.AddModelError(nameof(form.DueLocal), "Pick a valid deadline.");
            valid = false;
        }

        if (form.MonthSheetId is null)
        {
            ModelState.AddModelError(nameof(form.MonthSheetId), "Pick the month this task belongs to.");
            valid = false;
        }

        var folderPath = form.ExistingReferenceFolderPath;
        var fileCount = form.ExistingReferenceFileCount;
        var starterPath = form.ExistingStarterZipPath;
        var starterName = form.ExistingStarterZipFileName;
        var starterSize = form.ExistingStarterZipSizeBytes;

        if (form.ClearReference)
        {
            folderPath = null;
            fileCount = 0;
        }

        if (form.ClearStarterZip)
        {
            starterPath = null;
            starterName = null;
            starterSize = 0;
        }

        // Only touch the disk once the cheap fields are known to be good.
        if (valid && form.StarterZip is { Length: > 0 })
        {
            var upload = await starters.SaveAsync(form.StarterZip, cancellationToken);
            if (upload.Failed)
            {
                ModelState.AddModelError(nameof(form.StarterZip), upload.Message);
                valid = false;
            }
            else
            {
                starterPath = upload.Value!.FilePath;
                starterName = upload.Value.FileName;
                starterSize = upload.Value.SizeBytes;
            }
        }

        // Same disk-touch rule for the optional reference folder.
        if (valid && form.ReferenceFiles is { Count: > 0 })
        {
            var selected = form.ReferenceFiles
                .Where(file => !string.IsNullOrWhiteSpace(file.FileName))
                .ToList();

            if (selected.Count > 0)
            {
                var inputs = selected
                    .Select(file => new SubmissionInputFile(
                        file.FileName.Replace('\\', '/'),
                        file.Length,
                        _ => Task.FromResult(file.OpenReadStream())))
                    .ToList();

                var upload = await references.SaveAsync(inputs, cancellationToken);
                if (upload.Failed)
                {
                    ModelState.AddModelError(nameof(form.ReferenceFiles), upload.Message);
                    valid = false;
                }
                else
                {
                    folderPath = upload.Value!.FolderPath;
                    fileCount = upload.Value.FileCount;
                }
            }
        }

        if (!valid || form.MonthSheetId is null)
        {
            form.Request = null;
            return false;
        }

        // A retry after a validation error keeps whatever was just stored.
        form.ExistingReferenceFolderPath = folderPath;
        form.ExistingReferenceFileCount = fileCount;
        form.ExistingStarterZipPath = starterPath;
        form.ExistingStarterZipFileName = starterName;
        form.ExistingStarterZipSizeBytes = starterSize;

        form.Request = new CSharpTaskRequest(
            form.Title ?? string.Empty,
            form.DescriptionMd,
            form.MonthSheetId.Value,
            due,
            starterPath,
            starterName,
            starterSize,
            folderPath,
            fileCount,
            form.IsHidden,
            form.IsActive);

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

    private static CSharpTaskFormViewModel CloneWithId(CSharpTaskFormViewModel form, Guid id) => new()
    {
        Id = id,
        Title = form.Title,
        DescriptionMd = form.DescriptionMd,
        MonthSheetId = form.MonthSheetId,
        DueLocal = form.DueLocal,
        ReferenceFiles = form.ReferenceFiles,
        ExistingReferenceFolderPath = form.ExistingReferenceFolderPath,
        ExistingReferenceFileCount = form.ExistingReferenceFileCount,
        ClearReference = form.ClearReference,
        StarterZip = form.StarterZip,
        ExistingStarterZipPath = form.ExistingStarterZipPath,
        ExistingStarterZipFileName = form.ExistingStarterZipFileName,
        ExistingStarterZipSizeBytes = form.ExistingStarterZipSizeBytes,
        ClearStarterZip = form.ClearStarterZip,
        IsHidden = form.IsHidden,
        IsActive = form.IsActive
    };
}