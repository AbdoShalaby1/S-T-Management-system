using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Grading;
using Xunit;

namespace OscCommittee.Tests.Services.Grading;

public sealed class GradingRunnerTests
{
    private const string GoodAnswer = """
        {
          "score": 5,
          "summary": "Works.",
          "strengths": ["clear"],
          "issues": [{ "severity": "nit", "file": "Program.cs", "message": "note" }],
          "verdict": "Good"
        }
        """;

    [Fact]
    public async Task ASuccessfulFirstCallHasNoRetries()
    {
        var gemini = new FakeGemini(GeminiCallResultOk(GoodAnswer));

        var result = await Runner(gemini).GradeAsync("prompt");

        Assert.True(result.Success);
        Assert.Equal(5, result.Feedback!.Score);
        Assert.Equal(0, result.Retries);
        Assert.Equal(1, gemini.Calls);
    }

    [Fact]
    public async Task ARateLimitIsRetriedThenSucceeds()
    {
        var gemini = new FakeGemini(
            GeminiCallResult.Failed("busy", retryable: true, statusCode: 429),
            GeminiCallResultOk(GoodAnswer));

        var result = await Runner(gemini).GradeAsync("prompt");

        Assert.True(result.Success);
        Assert.Equal(1, result.Retries);
        Assert.Equal(2, gemini.Calls);
    }

    [Fact]
    public async Task PermanentServerFailuresStopAfterTwoRetries()
    {
        var gemini = new FakeGemini(
            GeminiCallResult.Failed("down", retryable: true, statusCode: 503));

        var result = await Runner(gemini).GradeAsync("prompt");

        Assert.False(result.Success);
        Assert.Equal(2, result.Retries);
        Assert.Equal(3, gemini.Calls);
        Assert.Contains("down", result.Error);
    }

    [Fact]
    public async Task ANonRetryableFailureStopsImmediately()
    {
        var gemini = new FakeGemini(
            GeminiCallResult.Failed("bad key", retryable: false, statusCode: 403));

        var result = await Runner(gemini).GradeAsync("prompt");

        Assert.False(result.Success);
        Assert.Equal(0, result.Retries);
        Assert.Equal(1, gemini.Calls);
    }

    [Fact]
    public async Task ANonNumericScoreIsRetried()
    {
        var gemini = new FakeGemini(
            GeminiCallResultOk("""{ "score": "great" }"""),
            GeminiCallResultOk(GoodAnswer));

        var result = await Runner(gemini).GradeAsync("prompt");

        Assert.True(result.Success);
        Assert.Equal(1, result.Retries);
        Assert.Equal(5, result.Feedback!.Score);
    }

    [Fact]
    public async Task AnUnusableScoreThatNeverRecoversFails()
    {
        var gemini = new FakeGemini(GeminiCallResultOk("""{ "score": "great" }"""));

        var result = await Runner(gemini).GradeAsync("prompt");

        Assert.False(result.Success);
        Assert.Equal(2, result.Retries);
        Assert.Equal(3, gemini.Calls);
    }

    [Fact]
    public async Task AnUnusableAnswerIsRetriedWithACorrection()
    {
        var gemini = new FakeGemini(
            GeminiCallResultOk("""{ "score": "great" }"""),
            GeminiCallResultOk(GoodAnswer));

        var result = await Runner(gemini).GradeAsync("BASE PROMPT");

        Assert.True(result.Success);
        Assert.Equal(1, result.Retries);
        Assert.Equal(2, gemini.Prompts.Count);
        Assert.Equal("BASE PROMPT", gemini.Prompts[0]);
        Assert.StartsWith("BASE PROMPT", gemini.Prompts[1]);
        Assert.Contains("=== CORRECTION ===", gemini.Prompts[1]);
        Assert.Contains("no usable score", gemini.Prompts[1]);
        Assert.Contains("""{ "score": "great" }""", gemini.Prompts[1]);
    }

    [Fact]
    public async Task TransportRetriesRepeatTheOriginalPrompt()
    {
        var gemini = new FakeGemini(
            GeminiCallResult.Failed("busy", retryable: true, statusCode: 429),
            GeminiCallResultOk(GoodAnswer));

        var result = await Runner(gemini).GradeAsync("BASE PROMPT");

        Assert.True(result.Success);
        Assert.Equal(2, gemini.Prompts.Count);
        Assert.All(gemini.Prompts, prompt => Assert.Equal("BASE PROMPT", prompt));
    }

    [Fact]
    public async Task ATruncatedAnswerRetriesWithACompactCorrection()
    {
        const string partial = "{\"score\":5,\"summary\":\"A long review that was cut";
        var gemini = new FakeGemini(
            new GeminiCallResult(
                true, partial, 200, null, false, 100, 8192, Truncated: true),
            GeminiCallResultOk(GoodAnswer));

        var result = await Runner(gemini).GradeAsync("BASE PROMPT");

        Assert.True(result.Success);
        Assert.Equal(1, result.Retries);
        Assert.Equal(2, gemini.Prompts.Count);
        Assert.Contains("cut off", gemini.Prompts[1]);
        Assert.Contains("Keep this answer compact", gemini.Prompts[1]);
        Assert.Contains(partial, gemini.Prompts[1]);
    }

    [Fact]
    public void TheRepairPromptTruncatesAVeryLongRejectedAnswer()
    {
        var longAnswer = new string('x', GradingRunner.MaxEchoedAnswerChars + 500);

        var prompt = GradingRunner.BuildRepairPrompt("BASE", "bad score", longAnswer);

        Assert.DoesNotContain(longAnswer, prompt);
        Assert.Contains(new string('x', GradingRunner.MaxEchoedAnswerChars), prompt);
        Assert.Contains("bad score", prompt);
    }

    private static GradingRunner Runner(FakeGemini gemini) => new(
        gemini,
        Options.Create(new AiOptions
        {
            ApiKey = "test-key",
            Model = "gemini-test",
            RetryDelaysSeconds = [0, 0]
        }),
        NullLogger<GradingRunner>.Instance);

    private static GeminiCallResult GeminiCallResultOk(string text) =>
        new(true, text, 200, null, false, 100, 20);

    private sealed class FakeGemini(params GeminiCallResult[] results) : IGeminiClient
    {
        public int Calls { get; private set; }

        public List<string> Prompts { get; } = [];

        public Task<GeminiCallResult> SendAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            var index = Math.Min(Calls, results.Length - 1);
            Calls++;
            Prompts.Add(prompt);
            return Task.FromResult(results[index]);
        }
    }
}