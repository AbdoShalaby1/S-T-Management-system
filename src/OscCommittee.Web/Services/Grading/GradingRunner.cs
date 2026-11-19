using System.Text;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Grading;

public sealed record GradingRunResult(
    bool Success,
    GradingFeedback? Feedback,
    string? Error,
    int Retries,
    int? InputTokens,
    int? OutputTokens,
    string Model)
{
    public static GradingRunResult Fail(string error, int retries, string model) =>
        new(false, null, error, retries, null, null, model);

    public static GradingRunResult Ok(
        GradingFeedback feedback,
        int retries,
        int? inputTokens,
        int? outputTokens,
        string model) =>
        new(true, feedback, null, retries, inputTokens, outputTokens, model);
}

public interface IGradingRunner
{
    Task<GradingRunResult> GradeAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// PLAN §10.3 retries: 429, 5xx, timeouts and unusable answers get two more attempts with the
/// configured backoff. A schema violation counts as unusable, so a 0/11/float answer is retried.
/// </summary>
public sealed class GradingRunner(
    IGeminiClient gemini,
    IOptions<AiOptions> options,
    ILogger<GradingRunner> logger) : IGradingRunner
{
    /// <summary>How much of a rejected answer is echoed back in a repair retry.</summary>
    public const int MaxEchoedAnswerChars = 2000;

    public async Task<GradingRunResult> GradeAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var ai = options.Value;
        var delays = ai.RetryDelaysSeconds ?? [];
        var attempt = 0;
        string? lastError = null;
        string? unusableReason = null;
        string? unusableAnswer = null;
        var unusableTruncated = false;
        var nextPrompt = prompt;

        while (true)
        {
            var call = await gemini.SendAsync(nextPrompt, cancellationToken);

            if (call.Success && !string.IsNullOrWhiteSpace(call.Text))
            {
                if (GradingFeedbackParser.TryParse(call.Text, out var feedback, out var parseError)
                    && feedback is not null)
                {
                    if (attempt > 0)
                    {
                        logger.LogInformation("Grading succeeded after {Retries} retr(ies).", attempt);
                    }

                    return GradingRunResult.Ok(
                        feedback,
                        attempt,
                        call.InputTokens,
                        call.OutputTokens,
                        ai.Model);
                }

                // The call worked but the answer broke the contract. Retry with a correction
                // instead of sending the identical prompt again.
                lastError = call.Truncated
                    ? "The previous answer was cut off at the output token limit."
                    : parseError ?? "The grader returned an unusable answer.";
                unusableReason = lastError;
                unusableAnswer = call.Text;
                unusableTruncated = call.Truncated;
                logger.LogWarning(
                    "Unusable grading answer on attempt {Attempt}: {Reason}",
                    attempt + 1,
                    lastError);
            }
            else
            {
                lastError = call.Error ?? "The grader did not answer.";
                unusableReason = call.Truncated ? lastError : null;
                unusableAnswer = call.Truncated ? call.Text : null;
                unusableTruncated = call.Truncated;
                if (!call.Retryable && !call.Truncated)
                {
                    return GradingRunResult.Fail(lastError, attempt, ai.Model);
                }
            }

            if (attempt >= delays.Count)
            {
                return GradingRunResult.Fail(lastError ?? "Grading failed.", attempt, ai.Model);
            }

            nextPrompt = unusableReason is null
                ? prompt
                : BuildRepairPrompt(prompt, unusableReason, unusableAnswer, unusableTruncated);

            var seconds = Math.Max(0, delays[attempt]);
            if (seconds > 0)
            {
                logger.LogInformation(
                    "Retrying grading in {Seconds} seconds ({Attempt}/{Max}).",
                    seconds,
                    attempt + 1,
                    delays.Count);

                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }

            attempt++;
        }
    }

    /// <summary>
    /// Repeats the task for a model that produced unusable JSON, names the rejection reason and
    /// echoes the rejected answer (truncated) so the next attempt can correct it.
    /// </summary>
    public static string BuildRepairPrompt(
        string prompt,
        string reason,
        string? rejectedAnswer,
        bool truncated = false)
    {
        var builder = new StringBuilder(prompt.Length + MaxEchoedAnswerChars + 900);
        builder.Append(prompt.TrimEnd('\n'));
        builder.Append("\n\n=== CORRECTION ===\n");
        builder.Append("Your previous answer was rejected: ").Append(reason).Append('\n');

        if (truncated)
        {
            builder.Append(
                "Keep this answer compact so it fits the output limit: `summary` under 120 words, " +
                "at most 3 short strengths, at most 5 short issues, and a verdict of one short " +
                "sentence.\n");
        }

        if (!string.IsNullOrWhiteSpace(rejectedAnswer))
        {
            var text = rejectedAnswer.Trim();
            if (text.Length > MaxEchoedAnswerChars)
            {
                text = text[..MaxEchoedAnswerChars];
            }

            builder.Append("Rejected answer:\n<<<REJECTED\n").Append(text).Append("\nREJECTED>>>\n");
        }

        builder.Append(
            "Answer again with one JSON object only, matching the schema exactly: an integer `score` " +
            "from 0 to 5, a boolean `error` (true only when the code cannot run: a compile-blocking " +
            "syntax error or an unhandled runtime error during normal use means the score is 0), a " +
            "`summary` string, up to 3 `strengths`, up to 5 `issues` (each with `severity` " +
            "must-fix/should-fix/nit, `file` and `message`) and a `verdict` string. " +
            "Do not wrap the JSON in Markdown fences and do not add commentary.");
        return builder.ToString();
    }
}