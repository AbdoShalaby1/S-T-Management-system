using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Web.Services.Tasks;

/// <summary>
/// Owns the folder upload pipeline. Files are stored under <c>Uploads:Root</c> with generated
/// paths only; the member's browser path never reaches the file system unnormalised.
/// </summary>
public sealed class CSharpSubmissionService(
    AppDbContext db,
    ICSharpTaskService tasks,
    IMarkdownBundleService bundles,
    ITimeZoneService timeZone,
    IWebHostEnvironment environment,
    IOptions<UploadsOptions> uploads,
    IOptions<AiOptions> ai,
    IGradingQueue gradingQueue,
    ILogger<CSharpSubmissionService> logger,
    TimeProvider timeProvider) : ICSharpSubmissionService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<Result<SubmissionReceipt>> SubmitAsync(
        Guid taskId,
        Guid userId,
        IReadOnlyList<SubmissionInputFile> files,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetVisibleAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Result.Fail<SubmissionReceipt>("not_found", "That task is no longer available.");
        }

        var plan = SubmissionPlanner.Plan(files, uploads.Value);
        if (!plan.Succeeded)
        {
            return Result.Fail<SubmissionReceipt>(
                plan.FailureCode ?? "invalid_upload",
                plan.FailureMessage ?? UploadCopy.NoFiles);
        }

        var memberName = await db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(memberName))
        {
            return Result.Fail<SubmissionReceipt>("member_missing", "Your account could not be found.");
        }

        List<string> contents;
        try
        {
            contents = await ReadContentsAsync(plan, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Reading an uploaded submission failed for user {UserId}.", userId);
            return Result.Fail<SubmissionReceipt>(
                "read_failed",
                "Some files could not be read. Upload the folder again.");
        }

        var now = timeProvider.GetUtcNow();
        var isLate = now > task.DueAtUtc;

        // One normal submission per member. The only second chance is a syntax error, whose
        // marker row is deleted here so the replacement is attempt #1 and no history remains.
        var previous = await db.CSharpSubmissions
            .Where(submission => submission.TaskId == taskId && submission.UserId == userId)
            .ToListAsync(cancellationToken);

        if (previous.Exists(submission => submission.Status != SubmissionStatus.TryAgain))
        {
            return Result.Fail<SubmissionReceipt>(
                "already_submitted",
                "You have already submitted this task. Ask the board if you need to replace it.");
        }

        var retryRows = previous
            .Where(submission => submission.Status == SubmissionStatus.TryAgain)
            .ToList();

        if (retryRows.Count > 0)
        {
            db.CSharpSubmissions.RemoveRange(retryRows);
            await db.SaveChangesAsync(cancellationToken);
        }

        var attemptNo = 1;

        var bundle = bundles.Build(new BundleInput(
            task.Title,
            memberName,
            task.MonthLabel,
            timeZone.FormatLocal(now, "yyyy-MM-dd HH:mm"),
            timeZone.TimeZone.Id,
            attemptNo,
            isLate,
            plan.NotKeptCount,
            plan.TotalBytes,
            plan.Kept
                .Select((file, index) => new BundleFile(file.Path, contents[index]))
                .ToList()));

        var submissionId = Guid.CreateVersion7();
        var folderRelative = $"{userId:N}/{submissionId:N}";
        var root = Path.Combine(environment.ContentRootPath, uploads.Value.Root);
        var folderFull = SubmissionPathRules.ResolveUnderRoot(root, folderRelative);

        if (folderFull is null)
        {
            return Result.Fail<SubmissionReceipt>("path_escape", UploadCopy.PathEscape);
        }

        var saved = false;
        try
        {
            Directory.CreateDirectory(folderFull);

            for (var index = 0; index < plan.Kept.Count; index++)
            {
                var target = SubmissionPathRules.ResolveUnderRoot(
                    Path.Combine(folderFull, "files"),
                    plan.Kept[index].Path);

                if (target is null)
                {
                    return Result.Fail<SubmissionReceipt>("path_escape", UploadCopy.PathEscape);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, contents[index], Utf8NoBom, cancellationToken);
            }

            var bundleFull = Path.Combine(folderFull, "bundle.md");
            await File.WriteAllTextAsync(bundleFull, bundle.Markdown, Utf8NoBom, cancellationToken);

            var submission = new CSharpSubmission
            {
                Id = submissionId,
                TaskId = taskId,
                UserId = userId,
                AttemptNo = attemptNo,
                StoredFolderPath = folderRelative,
                BundlePath = $"{folderRelative}/bundle.md",
                FileCount = plan.Kept.Count,
                IgnoredFileCount = plan.NotKeptCount,
                TotalBytes = plan.TotalBytes,
                BundleChars = bundle.Chars,
                Status = SubmissionStatus.Uploaded,
                IsLate = isLate,
                SubmittedAtUtc = now
            };

            db.CSharpSubmissions.Add(submission);
            await db.SaveChangesAsync(cancellationToken);
            saved = true;

            // The syntax-error attempt it replaces is gone from history; its files go too.
            foreach (var stale in retryRows)
            {
                var staleFolder = SubmissionPathRules.ResolveUnderRoot(root, stale.StoredFolderPath);
                if (staleFolder is not null)
                {
                    TryDeleteFolder(staleFolder);
                }
            }

            // PLAN §10.4 step 9: grading runs off-request; the upload never waits for the model.
            gradingQueue.Enqueue(submission.Id);

            return Result.Ok(
                new SubmissionReceipt(
                    submission.Id,
                    taskId,
                    submission.AttemptNo,
                    isLate,
                    plan.Kept.Count,
                    plan.NotKeptCount,
                    plan.DuplicateCount,
                    bundle.Chars),
                "Upload saved.");
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Saving a submission failed for user {UserId}.", userId);
            return Result.Fail<SubmissionReceipt>(
                "attempt_conflict",
                "Your previous upload was still saving. Try again in a moment.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Storing a submission failed for user {UserId}.", userId);
            return Result.Fail<SubmissionReceipt>(
                "storage_failed",
                "The upload could not be saved. Try again; if it keeps failing, tell the board.");
        }
        finally
        {
            if (!saved)
            {
                TryDeleteFolder(folderFull);
            }
        }
    }

    public async Task<IReadOnlyList<SubmissionAttemptRow>> GetAttemptsAsync(
        Guid taskId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.TaskId == taskId && submission.UserId == userId)
            .OrderByDescending(submission => submission.AttemptNo)
            .Select(submission => new
            {
                submission.Id,
                submission.AttemptNo,
                submission.SubmittedAtUtc,
                submission.FileCount,
                submission.IsLate,
                submission.Status,
                submission.Score,
                submission.AiScore,
                submission.IsManualScore,
                submission.Summary,
                submission.Verdict
            })
            .ToListAsync(cancellationToken);

        var points = rows.ToDictionary(
            row => row.Id,
            row => SubmissionScoring.EffectivePoints(row.Score, row.IsLate, row.IsManualScore));

        var bestId = rows
            .Where(row => points[row.Id].HasValue)
            .OrderByDescending(row => points[row.Id]!.Value)
            .ThenBy(row => row.SubmittedAtUtc)
            .Select(row => row.Id)
            .FirstOrDefault();

        return rows
            .Select(row => new SubmissionAttemptRow(
                row.Id,
                row.AttemptNo,
                row.SubmittedAtUtc,
                row.FileCount,
                row.IsLate,
                row.Status,
                row.Score,
                row.AiScore,
                points[row.Id],
                row.Summary,
                row.Verdict,
                row.Id == bestId))
            .ToList();
    }

    public async Task<SubmissionDetail?> FindForMemberAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.Id == submissionId && submission.UserId == userId)
            .Select(submission => new
            {
                submission.Id,
                submission.TaskId,
                submission.AttemptNo,
                submission.SubmittedAtUtc,
                submission.IsLate,
                submission.Status,
                submission.Score,
                submission.IsManualScore,
                submission.Summary,
                submission.Verdict,
                submission.ErrorMessage,
                submission.GradedAtUtc,
                submission.FeedbackJson,
                TaskTitle = submission.Task!.Title,
                Year = submission.Task.MonthSheet!.Year,
                Month = submission.Task.MonthSheet.Month
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new SubmissionDetail(
            row.Id,
            row.TaskId,
            row.TaskTitle,
            MonthLabels.Format(row.Year, row.Month),
            row.AttemptNo,
            row.SubmittedAtUtc,
            row.IsLate,
            row.Status,
            row.Score,
            SubmissionScoring.EffectivePoints(row.Score, row.IsLate, row.IsManualScore),
            row.Summary,
            row.Verdict,
            row.ErrorMessage,
            row.GradedAtUtc,
            !string.IsNullOrWhiteSpace(row.FeedbackJson) || !string.IsNullOrWhiteSpace(row.Summary),
            GradingFeedbackJson.Deserialize(row.FeedbackJson));
    }

    public async Task<SubmissionStatusSnapshot?> GetStatusAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.Id == submissionId && submission.UserId == userId)
            .Select(submission => new
            {
                submission.Id,
                submission.Status,
                submission.Score,
                submission.IsLate,
                submission.IsManualScore,
                submission.ErrorMessage,
                submission.GradedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new SubmissionStatusSnapshot(
                row.Id,
                row.Status,
                row.Score,
                SubmissionScoring.EffectivePoints(row.Score, row.IsLate, row.IsManualScore),
                row.IsLate,
                ai.Value.IsConfigured,
                row.ErrorMessage,
                row.GradedAtUtc);
    }

    public async Task<IReadOnlyList<TaskAttemptSummary>> GetTaskSummariesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.UserId == userId)
            .Select(submission => new
            {
                submission.TaskId,
                submission.AttemptNo,
                submission.Score,
                submission.IsLate,
                submission.IsManualScore,
                submission.Status
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.TaskId)
            .Select(group =>
            {
                var best = group
                    .Select(row => new
                    {
                        row.Score,
                        Points = SubmissionScoring.EffectivePoints(row.Score, row.IsLate, row.IsManualScore)
                    })
                    .Where(row => row.Points.HasValue)
                    .OrderByDescending(row => row.Points!.Value)
                    .FirstOrDefault();

                var latest = group.OrderByDescending(row => row.AttemptNo).First();

                return new TaskAttemptSummary(
                    group.Key,
                    group.Count(),
                    best?.Score,
                    best?.Points,
                    latest.Status);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<BoardSubmissionRow>> GetBoardAttemptsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.TaskId == taskId)
            .OrderByDescending(submission => submission.SubmittedAtUtc)
            .Select(submission => new
            {
                submission.Id,
                submission.UserId,
                MemberName = submission.User!.DisplayName,
                submission.AttemptNo,
                submission.SubmittedAtUtc,
                submission.FileCount,
                submission.IsLate,
                submission.Status,
                submission.Score,
                submission.IsManualScore
            })
            .ToListAsync(cancellationToken);

        var bestByMember = rows
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => CSharpCreditRules.Select(group.Select(row => new CreditableAttempt(
                    row.Id,
                    row.AttemptNo,
                    row.Score,
                    row.IsLate,
                    row.IsManualScore,
                    row.SubmittedAtUtc)))?.Id);

        return rows
            .Select(row => new BoardSubmissionRow(
                row.Id,
                row.UserId,
                row.MemberName,
                row.AttemptNo,
                row.SubmittedAtUtc,
                row.FileCount,
                row.IsLate,
                row.Status,
                row.Score,
                row.IsManualScore
                    ? (row.Score is null ? null : Math.Clamp(row.Score.Value, 1, SubmissionScoring.MaxPoints))
                    : SubmissionScoring.Points(row.Score, row.IsLate),
                bestByMember.GetValueOrDefault(row.UserId) == row.Id))
            .ToList();
    }

    private static async Task<List<string>> ReadContentsAsync(
        SubmissionPlan plan,
        CancellationToken cancellationToken)
    {
        var contents = new List<string>(plan.Kept.Count);

        foreach (var file in plan.Kept)
        {
            await using var stream = await file.OpenAsync(cancellationToken);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);

            contents.Add(await reader.ReadToEndAsync(cancellationToken));
        }

        return contents;
    }

    private void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove the failed submission folder {Folder}.", folder);
        }
    }
}