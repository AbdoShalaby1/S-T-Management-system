using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Web.Services.Grading;

/// <summary>
/// PLAN §10.3 and §12.2: two background workers drain the grading queue. Each attempt moves
/// Uploaded → Grading → Graded/Failed; the upload itself never waits for Gemini.
/// </summary>
public sealed class GradingWorker(
    IGradingQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<AiOptions> aiOptions,
    IOptions<UploadsOptions> uploadOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<GradingWorker> logger) : BackgroundService
{
    public const int WorkerCount = 2;

    /// <summary>Cap on the reference text handed to the model.</summary>
    public const int ReferencePromptMaxChars = 200_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeuePendingAsync(stoppingToken);

        var workers = Enumerable.Range(1, WorkerCount)
            .Select(index => RunWorkerAsync(index, stoppingToken))
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RunWorkerAsync(int index, CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Grading worker {Worker} failed on submission {SubmissionId}.",
                    index,
                    item.SubmissionId);
            }
        }
    }

    /// <summary>Rows left Uploaded (or Grading after a crash) are queued again on startup.</summary>
    private async Task RequeuePendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.CSharpSubmissions
            .Where(submission =>
                submission.Status == SubmissionStatus.Uploaded
                || submission.Status == SubmissionStatus.Grading)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var submission in pending)
        {
            submission.Status = SubmissionStatus.Uploaded;
            submission.ErrorMessage = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var submission in pending)
        {
            queue.Enqueue(submission.Id);
        }

        logger.LogInformation("Requeued {Count} submission(s) after startup.", pending.Count);
    }

    private async Task ProcessAsync(GradingWorkItem item, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prompts = scope.ServiceProvider.GetRequiredService<IPromptBuilder>();
        var runner = scope.ServiceProvider.GetRequiredService<IGradingRunner>();
        var references = scope.ServiceProvider.GetRequiredService<IReferenceFileService>();

        var submission = await db.CSharpSubmissions
            .Include(row => row.Task)
            .ThenInclude(task => task!.MonthSheet)
            .Include(row => row.User)
            .FirstOrDefaultAsync(row => row.Id == item.SubmissionId, cancellationToken);

        if (submission is null)
        {
            return;
        }

        // A second queue entry for the same attempt waits for the first one to finish.
        if (!item.Force && submission.Status is SubmissionStatus.Graded
            or SubmissionStatus.Grading
            or SubmissionStatus.AwaitingReview)
        {
            return;
        }

        submission.Status = SubmissionStatus.Grading;
        submission.ErrorMessage = null;
        submission.RetryCount = 0;
        await db.SaveChangesAsync(cancellationToken);

        if (!aiOptions.Value.IsConfigured)
        {
            await FailAsync(
                db,
                submission,
                "Grading is not set up yet. Your files are saved; the board can grade this attempt once it is configured.",
                0,
                cancellationToken);

            return;
        }

        var bundle = await ReadBundleAsync(submission, cancellationToken);
        if (bundle is null)
        {
            await FailAsync(
                db,
                submission,
                "The stored files could not be read. Upload the folder again.",
                0,
                cancellationToken);

            return;
        }

        submission.BundleHash = Hash(bundle);
        submission.InputChars = bundle.Length;

        if (!item.Force)
        {
            var reused = await TryReuseAsync(db, submission, cancellationToken);
            if (reused)
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var task = submission.Task!;
        var reference = await references.ReadPromptTextAsync(
            task.ReferenceFolderPath,
            ReferencePromptMaxChars,
            cancellationToken);

        var prompt = await prompts.BuildAsync(
            new PromptRequest(
                task.Title,
                MonthLabels.Format(task.MonthSheet!.Year, task.MonthSheet.Month),
                task.DescriptionMd,
                reference,
                bundle),
            cancellationToken);

        var run = await runner.GradeAsync(prompt.Text, cancellationToken);
        var now = timeProvider.GetUtcNow();

        submission.PromptHash = prompt.Hash;
        submission.PromptVersion = prompt.Version;
        submission.InputChars = prompt.InputChars;

        if (!run.Success || run.Feedback is null)
        {
            await FailAsync(
                db,
                submission,
                run.Error ?? "Grading failed. Your files are saved; the board can retry this attempt.",
                run.Retries,
                cancellationToken);

            return;
        }

        var feedback = run.Feedback;

        if (feedback.Error)
        {
            // The code does not compile. This is not a normal attempt: score 0, nothing credited,
            // and the member may replace it. The row is removed when they upload again.
            submission.Status = SubmissionStatus.TryAgain;
            submission.Score = null;
            submission.AiScore = 0;
            submission.IsManualScore = false;
            submission.FeedbackJson = GradingFeedbackJson.Serialize(feedback);
            submission.Summary = string.IsNullOrWhiteSpace(feedback.Summary)
                ? "The code has a syntax error and did not compile. Fix it and upload the folder again."
                : feedback.Summary;
            submission.Verdict = string.IsNullOrWhiteSpace(feedback.Verdict)
                ? "Fix and upload again"
                : feedback.Verdict;
            submission.GradedAtUtc = null;
            submission.GradingModel = Truncate(run.Model, 100);
            submission.InputTokens = run.InputTokens;
            submission.OutputTokens = run.OutputTokens;
            submission.RetryCount = run.Retries;
            submission.ErrorMessage = null;

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Marked submission {SubmissionId} as a syntax error; the member can upload again.",
                submission.Id);

            return;
        }

        // The AI produces a draft only. A board member accepts it (and may edit the score and
        // wording) before anything is credited or shown to the member.
        submission.Status = SubmissionStatus.AwaitingReview;
        submission.Score = null;
        submission.AiScore = feedback.Score;
        submission.IsManualScore = false;
        submission.FeedbackJson = GradingFeedbackJson.Serialize(feedback);
        submission.Summary = string.IsNullOrWhiteSpace(feedback.Summary)
            ? feedback.Verdict
            : feedback.Summary;
        submission.Verdict = feedback.Verdict;
        submission.GradedAtUtc = null;
        submission.GradingModel = Truncate(run.Model, 100);
        submission.InputTokens = run.InputTokens;
        submission.OutputTokens = run.OutputTokens;
        submission.RetryCount = run.Retries;
        submission.ErrorMessage = null;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Graded submission {SubmissionId} as a {Score}/{Max} draft after {Retries} retr(ies); awaiting board review.",
            submission.Id,
            feedback.Score,
            GradingScore.Maximum,
            run.Retries);
    }

    private async Task<string?> ReadBundleAsync(
        CSharpSubmission submission,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(environment.ContentRootPath, uploadOptions.Value.Root);
        var fullPath = SubmissionPathRules.ResolveUnderRoot(root, submission.BundlePath);

        if (fullPath is null || !File.Exists(fullPath))
        {
            logger.LogWarning("Bundle missing for submission {SubmissionId}.", submission.Id);
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Reading bundle {Path} failed.", fullPath);
            return null;
        }
    }

    /// <summary>
    /// PLAN §10.3 cost control: an identical bundle for the same member and task reuses the
    /// previous result instead of calling the model again. A board retry sets <c>Force</c>.
    /// </summary>
    private async Task<bool> TryReuseAsync(
        AppDbContext db,
        CSharpSubmission submission,
        CancellationToken cancellationToken)
    {
        var reusable = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(row =>
                row.Id != submission.Id
                && row.TaskId == submission.TaskId
                && row.UserId == submission.UserId
                && row.Status == SubmissionStatus.Graded
                && row.BundleHash == submission.BundleHash
                && row.Score != null
                && row.FeedbackJson != null)
            .OrderByDescending(row => row.GradedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (reusable is null)
        {
            return false;
        }

        submission.Status = SubmissionStatus.AwaitingReview;
        submission.Score = null;
        submission.AiScore = reusable.AiScore ?? reusable.Score;
        submission.IsManualScore = false;
        submission.FeedbackJson = reusable.FeedbackJson;
        submission.Summary = reusable.Summary;
        submission.Verdict = reusable.Verdict;
        submission.GradedAtUtc = null;
        submission.GradingModel = reusable.GradingModel;
        submission.PromptHash = null;
        submission.PromptVersion = reusable.PromptVersion;
        submission.InputTokens = null;
        submission.OutputTokens = null;
        submission.RetryCount = 0;
        submission.ErrorMessage = null;

        return true;
    }

    private async Task FailAsync(
        AppDbContext db,
        CSharpSubmission submission,
        string message,
        int retries,
        CancellationToken cancellationToken)
    {
        submission.Status = SubmissionStatus.Failed;
        submission.ErrorMessage = Truncate(message, 1000);
        submission.RetryCount = retries;
        submission.GradedAtUtc = null;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Grading failed for submission {SubmissionId}: {Message}",
            submission.Id,
            submission.ErrorMessage);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}