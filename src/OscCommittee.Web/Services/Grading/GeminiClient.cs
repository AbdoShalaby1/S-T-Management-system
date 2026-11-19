using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Grading;

/// <summary>One Gemini call. <see cref="Retryable"/> is true for 429, 5xx, timeouts and empty answers.</summary>
public sealed record GeminiCallResult(
    bool Success,
    string? Text,
    int StatusCode,
    string? Error,
    bool Retryable,
    int? InputTokens,
    int? OutputTokens,
    bool Truncated = false)
{
    public static GeminiCallResult Failed(string error, bool retryable, int statusCode = 0) =>
        new(false, null, statusCode, error, retryable, null, null);
}

public interface IGeminiClient
{
    Task<GeminiCallResult> SendAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// PLAN §10.3: the Gemini <c>generateContent</c> endpoint with a JSON response schema. The API key
/// travels in the <c>x-goog-api-key</c> header and is never logged.
/// </summary>
public sealed class GeminiClient(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    ILogger<GeminiClient> logger) : IGeminiClient
{
    public const string HttpClientName = "gemini";

    /// <summary>Reported when the model stops at the output token limit mid-answer.</summary>
    public const string TruncatedError = "The grader's answer was cut off before it finished.";

    public async Task<GeminiCallResult> SendAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var ai = options.Value;
        if (!ai.IsConfigured)
        {
            return GeminiCallResult.Failed("AI not configured.", retryable: false);
        }

        var model = ai.Model.Trim();
        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            model = model["models/".Length..];
        }

        var payload = BuildPayload(prompt, ai);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("x-goog-api-key", ai.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The grader timed out after {Seconds} seconds.", ai.TimeoutSeconds);
            return GeminiCallResult.Failed("The grader timed out.", retryable: true);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The grader request failed.");
            return GeminiCallResult.Failed("The grader could not be reached.", retryable: true);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500;
                var message = status switch
                {
                    400 => "The grader rejected the request (HTTP 400).",
                    401 or 403 => "The grader API key was rejected.",
                    404 => "The configured AI model was not found.",
                    429 => "The grader is busy (HTTP 429).",
                    >= 500 => "The grader service is unavailable.",
                    _ => $"The grader returned HTTP {status}."
                };

                logger.LogWarning(
                    "Grading request failed with HTTP {Status} (retryable: {Retryable}).",
                    status,
                    retryable);

                return GeminiCallResult.Failed(message, retryable, status);
            }

            return ParseBody(body);
        }
    }

    /// <summary>The structured-output schema from PLAN §10.3. Score 0 is reserved for a syntax error.</summary>
    public static JsonObject CreateSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["score"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 0,
                ["maximum"] = GradingScore.Maximum
            },
            ["error"] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = "True when the code cannot run because of an error (syntax or runtime). Score is then 0."
            },
            ["summary"] = new JsonObject { ["type"] = "string" },
            ["strengths"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["maxItems"] = 3
            },
            ["issues"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = 5,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["severity"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("must-fix", "should-fix", "nit")
                        },
                        ["file"] = new JsonObject { ["type"] = "string" },
                        ["message"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray("severity", "file", "message")
                }
            },
            ["verdict"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("score", "error", "summary", "strengths", "issues", "verdict")
    };

    private static JsonObject BuildPayload(string prompt, AiOptions ai) => new()
    {
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = prompt } }
            }
        },
        ["generationConfig"] = new JsonObject
        {
            ["temperature"] = ai.Temperature,

            // No maxOutputTokens here on purpose: the API's own model budget is the only limit.
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = CreateSchema()
        }
    };

    private GeminiCallResult ParseBody(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("promptFeedback", out var feedback)
                && feedback.TryGetProperty("blockReason", out _))
            {
                return GeminiCallResult.Failed(
                    "The grader declined to review this submission.",
                    retryable: false);
            }

            var inputTokens = ReadTokenCount(root, "promptTokenCount");
            var outputTokens = ReadTokenCount(root, "candidatesTokenCount");
            var finishReason = ReadFinishReason(root);
            var text = ExtractText(root);

            if (string.Equals(finishReason, "MAX_TOKENS", StringComparison.Ordinal))
            {
                // The JSON is normally incomplete here. Keep whatever text arrived so the retry
                // can show the model where it stopped, and let the runner ask for a shorter
                // answer instead of repeating the request that just ran out of budget.
                logger.LogWarning("The grader stopped at the output token limit.");

                var hasText = !string.IsNullOrWhiteSpace(text);
                return new GeminiCallResult(
                    hasText,
                    text,
                    200,
                    hasText ? null : TruncatedError,
                    Retryable: !hasText,
                    inputTokens,
                    outputTokens,
                    Truncated: true);
            }

            if (finishReason is "SAFETY" or "RECITATION" or "BLOCKLIST"
                or "PROHIBITED_CONTENT" or "SPII")
            {
                logger.LogWarning("The grader stopped with finish reason {Reason}.", finishReason);
                return GeminiCallResult.Failed(
                    "The grader declined to review this submission.",
                    retryable: false);
            }

            if (finishReason is "MALFORMED_FUNCTION_CALL" or "OTHER")
            {
                logger.LogWarning("The grader stopped with finish reason {Reason}.", finishReason);
                return GeminiCallResult.Failed(
                    "The grader returned an unusable answer.",
                    retryable: true);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return GeminiCallResult.Failed("The grader returned an empty answer.", retryable: true);
            }

            return new GeminiCallResult(true, text, 200, null, false, inputTokens, outputTokens);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "The grader response was not JSON.");
            return GeminiCallResult.Failed("The grader returned an unreadable answer.", retryable: true);
        }
    }

    private static int? ReadTokenCount(JsonElement root, string name)
    {
        if (root.TryGetProperty("usageMetadata", out var usage)
            && usage.TryGetProperty(name, out var tokens)
            && tokens.TryGetInt32(out var count))
        {
            return count;
        }

        return null;
    }

    private static string? ReadFinishReason(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var first = candidates[0];
        return first.TryGetProperty("finishReason", out var reason)
               && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()
            : null;
    }

    private static string? ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }
        }

        return null;
    }
}