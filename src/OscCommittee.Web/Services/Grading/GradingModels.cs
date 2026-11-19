using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OscCommittee.Web.Services.Grading;

/// <summary>Issue buckets from PLAN §10.3. Stored and sent as <c>must-fix</c> / <c>should-fix</c> / <c>nit</c>.</summary>
public enum GradingIssueSeverity
{
    MustFix = 0,
    ShouldFix = 1,
    Nit = 2
}

public sealed record GradingIssue(GradingIssueSeverity Severity, string File, string Message);

/// <summary>The structured answer the grader must return. The score is 1–5, or 0 for an error.</summary>
public sealed record GradingFeedback(
    int Score,
    string Summary,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<GradingIssue> Issues,
    string Verdict,
    bool Error = false);

/// <summary>Score normalisation from PLAN §10.3: round, then clamp; floats never leave this type.</summary>
public static class GradingScore
{
    public const int Minimum = 1;

    public const int Maximum = 5;

    /// <summary>Rounds half away from zero (4.5 → 5) and clamps into 1–5.</summary>
    public static int Normalize(double raw) =>
        Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), Minimum, Maximum);

    /// <summary>
    /// Reads a score whether the model sent a number or a numeric string. Returns false when the
    /// value is missing, not a number, or not finite, so the caller can retry instead of guessing.
    /// </summary>
    public static bool TryRead(JsonElement element, out int score)
    {
        score = 0;

        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDouble(out var number) && TryNormalize(number, out score);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            return double.TryParse(
                       text,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out var number)
                   && TryNormalize(number, out score);
        }

        return false;
    }

    private static bool TryNormalize(double number, out int score)
    {
        score = 0;
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            return false;
        }

        score = Normalize(number);
        return true;
    }
}

/// <summary>Turns the model's JSON into a bounded, typed feedback object (PLAN §10.3).</summary>
public static class GradingFeedbackParser
{
    public const int MaxStrengths = 3;

    public const int MaxIssues = 5;

    public static bool TryParse(string? json, out GradingFeedback? feedback, out string? error)
    {
        feedback = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The grader returned an empty answer.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = "The grader returned an unreadable answer.";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The grader returned an unreadable answer.";
                return false;
            }

            // A submission that does not compile is an automatic 0 and an invitation to retry.
            // The model may leave the score out in that case, so it is not required.
            var isError = ReadError(root);

            int score;
            if (isError)
            {
                score = 0;
            }
            else if (!root.TryGetProperty("score", out var scoreElement)
                     || !GradingScore.TryRead(scoreElement, out score))
            {
                error = "The grader returned no usable score.";
                return false;
            }

            var strengths = new List<string>();
            if (root.TryGetProperty("strengths", out var strengthsElement)
                && strengthsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in strengthsElement.EnumerateArray())
                {
                    if (strengths.Count >= MaxStrengths)
                    {
                        break;
                    }

                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            strengths.Add(text);
                        }
                    }
                }
            }

            var issues = new List<GradingIssue>();
            if (root.TryGetProperty("issues", out var issuesElement)
                && issuesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issuesElement.EnumerateArray())
                {
                    if (issues.Count >= MaxIssues)
                    {
                        break;
                    }

                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var message = StringValue(item, "message");
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        continue;
                    }

                    issues.Add(new GradingIssue(
                        ParseSeverity(StringValue(item, "severity")),
                        StringValue(item, "file") ?? string.Empty,
                        message));
                }
            }

            // Highest severity first, matching the prompt's "ordered by severity" rule.
            issues = issues
                .OrderBy(issue => issue.Severity)
                .ToList();

            feedback = new GradingFeedback(
                score,
                StringValue(root, "summary") ?? string.Empty,
                strengths,
                issues,
                StringValue(root, "verdict") ?? string.Empty,
                isError);

            return true;
        }
    }

    /// <summary>
    /// Reads the <c>error</c> flag, accepting a boolean or a boolean-like string so a model
    /// that writes "true" instead of true still produces a usable answer.
    /// </summary>
    public static bool ReadError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => element.TryGetDouble(out var number) && number != 0,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var flag) && flag,
            _ => false
        };
    }

    /// <summary>Buckets any wording the model chose into the three severities we display.</summary>
    public static GradingIssueSeverity ParseSeverity(string? raw)
    {
        var value = raw ?? string.Empty;

        if (value.Contains("must", StringComparison.OrdinalIgnoreCase))
        {
            return GradingIssueSeverity.MustFix;
        }

        return value.Contains("should", StringComparison.OrdinalIgnoreCase)
            ? GradingIssueSeverity.ShouldFix
            : GradingIssueSeverity.Nit;
    }

    private static string? StringValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}

/// <summary>Canonical storage format for <c>CSharpSubmission.FeedbackJson</c>.</summary>
public static class GradingFeedbackJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public static string Serialize(GradingFeedback feedback) =>
        JsonSerializer.Serialize(feedback, Options);

    public static GradingFeedback? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GradingFeedback>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Display wording for issue severities.</summary>
public static class GradingSeverityView
{
    public static string Label(GradingIssueSeverity severity) => severity switch
    {
        GradingIssueSeverity.MustFix => "Must fix",
        GradingIssueSeverity.ShouldFix => "Should fix",
        _ => "Nit"
    };

    public static string BadgeClass(GradingIssueSeverity severity) => severity switch
    {
        GradingIssueSeverity.MustFix => "badge-hard",
        GradingIssueSeverity.ShouldFix => "badge-medium",
        _ => "badge-hidden"
    };
}