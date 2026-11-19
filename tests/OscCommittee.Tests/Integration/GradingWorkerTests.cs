using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// PLAN §15/§18: the background worker drives the status machine and credits points. Gemini is
/// stubbed through DI, so success, throttling, a permanent outage and a non-retryable error can
/// each be exercised without the network.
/// </summary>
public sealed class GradingWorkerTests
{
    private const string GoodAnswer = """
        {
          "score": 5,
          "summary": "Works well.",
          "strengths": ["clear"],
          "issues": [{ "severity": "nit", "file": "Program.cs", "message": "note" }],
          "verdict": "Good"
        }
        """;

    [Fact]
    public async Task A_successful_grade_creates_a_draft_and_credits_nothing()
    {
        var gemini = new QueueGeminiClient(Ok(GoodAnswer));
        await using var app = new OscAppFactory(gemini);

        var outcome = await RunAsync(app);

        Assert.Equal(SubmissionStatus.AwaitingReview, outcome.Status);
        Assert.Null(outcome.Score);
        Assert.Equal(5, outcome.AiScore);
        Assert.Null(outcome.Points);
        Assert.Equal(0, outcome.RetryCount);
        Assert.Null(outcome.Error);
        Assert.Equal(1, gemini.Calls);
    }

    [Fact]
    public async Task Accepting_the_draft_publishes_the_score_and_credits_points()
    {
        var gemini = new QueueGeminiClient(Ok(GoodAnswer));
        await using var app = new OscAppFactory(gemini);

        var draft = await RunAsync(app);
        Assert.Equal(SubmissionStatus.AwaitingReview, draft.Status);

        var accepted = await AcceptAsync(app, draft.SubmissionId, 4, "Board-edited summary.");

        Assert.Equal(SubmissionStatus.Graded, accepted.Status);
        Assert.Equal(4, accepted.Score);
        Assert.Equal(4, accepted.Points);
        Assert.Equal("Board-edited summary.", accepted.Summary);
    }

    [Fact]
    public async Task A_rate_limited_call_is_retried_then_drafted()
    {
        var gemini = new QueueGeminiClient(
            GeminiCallResult.Failed("busy", retryable: true, statusCode: 429),
            Ok(GoodAnswer));
        await using var app = new OscAppFactory(gemini);

        var outcome = await RunAsync(app);

        Assert.Equal(SubmissionStatus.AwaitingReview, outcome.Status);
        Assert.Null(outcome.Score);
        Assert.Equal(5, outcome.AiScore);
        Assert.Equal(1, outcome.RetryCount);
        Assert.Equal(2, gemini.Calls);
    }

    [Fact]
    public async Task A_permanent_outage_fails_after_the_configured_retries()
    {
        var gemini = new QueueGeminiClient(
            GeminiCallResult.Failed("down", retryable: true, statusCode: 503));
        await using var app = new OscAppFactory(gemini);

        var outcome = await RunAsync(app);

        Assert.Equal(SubmissionStatus.Failed, outcome.Status);
        Assert.Equal(2, outcome.RetryCount);
        Assert.Equal(3, gemini.Calls);
        Assert.Contains("down", outcome.Error);
        Assert.Null(outcome.Points);
    }

    [Fact]
    public async Task A_syntax_error_scores_zero_and_invites_another_upload()
    {
        const string syntaxAnswer = """
            {
              "score": 0,
              "error": true,
              "summary": "Program.cs line 3: missing semicolon. Fix it and upload the folder again.",
              "strengths": [],
              "issues": [],
              "verdict": "Fix and upload again"
            }
            """;

        var gemini = new QueueGeminiClient(Ok(syntaxAnswer));
        await using var app = new OscAppFactory(gemini);

        var outcome = await RunAsync(app);

        Assert.Equal(SubmissionStatus.TryAgain, outcome.Status);
        Assert.Null(outcome.Score);
        Assert.Equal(0, outcome.AiScore);
        Assert.Null(outcome.Points);
        Assert.Contains("upload the folder again", outcome.Summary);
    }

    [Fact]
    public async Task A_non_retryable_error_stops_immediately()
    {
        var gemini = new QueueGeminiClient(
            GeminiCallResult.Failed("bad key", retryable: false, statusCode: 403));
        await using var app = new OscAppFactory(gemini);

        var outcome = await RunAsync(app);

        Assert.Equal(SubmissionStatus.Failed, outcome.Status);
        Assert.Equal(0, outcome.RetryCount);
        Assert.Equal(1, gemini.Calls);
        Assert.Contains("bad key", outcome.Error);
    }

    private static async Task<Outcome> RunAsync(OscAppFactory app)
    {
        await app.InitializeAsync();

        var member = await app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("graded"),
            "Graded Member");
        var taskId = await app.CreateTaskAsync(
            "Grading " + Guid.NewGuid().ToString("N")[..8],
            DateTimeOffset.UtcNow.AddDays(7),
            descriptionMd: "## Build it\n\nWrite a console app that prints a greeting.");
        var submissionId = await app.CreateSubmissionAsync(taskId, member.Id);

        // Submit already queued one item; drain it so only the worker's startup requeue remains.
        var queue = app.Services.GetRequiredService<IGradingQueue>();
        while (queue.Reader.TryRead(out _))
        {
        }

        var services = app.Services;
        using var worker = new GradingWorker(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<AiOptions>>(),
            services.GetRequiredService<IOptions<UploadsOptions>>(),
            services.GetRequiredService<IHostEnvironment>(),
            services.GetRequiredService<TimeProvider>(),
            NullLogger<GradingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        var outcome = await WaitForTerminalAsync(app, submissionId);

        // Give any follow-up work after the status change a moment, then stop the readers.
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        return outcome;
    }

    private static async Task<Outcome> WaitForTerminalAsync(OscAppFactory app, Guid submissionId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.CSharpSubmissions
                .AsNoTracking()
                .SingleAsync(submission => submission.Id == submissionId);

            if (row.Status == SubmissionStatus.Failed)
            {
                return new Outcome(
                    row.Id, row.Status, row.Score, row.AiScore, row.RetryCount, row.ErrorMessage,
                    null, row.Summary, Convert.ToBase64String(row.RowVersion));
            }

            if (row.Status == SubmissionStatus.AwaitingReview)
            {
                return new Outcome(
                    row.Id, row.Status, row.Score, row.AiScore, row.RetryCount, row.ErrorMessage,
                    null, row.Summary, Convert.ToBase64String(row.RowVersion));
            }

            if (row.Status == SubmissionStatus.TryAgain)
            {
                return new Outcome(
                    row.Id, row.Status, row.Score, row.AiScore, row.RetryCount, row.ErrorMessage,
                    null, row.Summary, Convert.ToBase64String(row.RowVersion));
            }

            if (row.Status == SubmissionStatus.Graded)
            {
                var entry = await db.PointEntries
                    .AsNoTracking()
                    .FirstOrDefaultAsync(entry => entry.Category == PointCategory.CSharpSubmission);

                if (entry is not null)
                {
                    return new Outcome(
                        row.Id, row.Status, row.Score, row.AiScore, row.RetryCount, row.ErrorMessage,
                        entry.FinalPoints, row.Summary, Convert.ToBase64String(row.RowVersion));
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The grading worker did not finish in time.");
    }

    private static async Task<Outcome> AcceptAsync(
        OscAppFactory app,
        Guid submissionId,
        int score,
        string summary)
    {
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBoardSubmissionService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // SQLite has no server-generated rowversion, so seed a value for the concurrency check.
        var tracked = await db.CSharpSubmissions
            .SingleAsync(submission => submission.Id == submissionId);

        tracked.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await db.SaveChangesAsync();

        var result = await service.SaveScoreAsync(
            submissionId,
            new BoardScoreEdit(score, summary, Convert.ToBase64String(tracked.RowVersion)),
            Guid.NewGuid());

        Assert.True(result.Succeeded, result.Message);

        var saved = await db.CSharpSubmissions
            .AsNoTracking()
            .SingleAsync(submission => submission.Id == submissionId);

        var entry = await db.PointEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Category == PointCategory.CSharpSubmission);

        return new Outcome(
            saved.Id, saved.Status, saved.Score, saved.AiScore, saved.RetryCount, saved.ErrorMessage,
            entry?.FinalPoints, saved.Summary, Convert.ToBase64String(saved.RowVersion));
    }

    private static GeminiCallResult Ok(string text) =>
        new(true, text, 200, null, false, 100, 20);

    private sealed record Outcome(
        Guid SubmissionId,
        SubmissionStatus Status,
        int? Score,
        int? AiScore,
        int RetryCount,
        string? Error,
        int? Points,
        string? Summary,
        string RowVersion);
}