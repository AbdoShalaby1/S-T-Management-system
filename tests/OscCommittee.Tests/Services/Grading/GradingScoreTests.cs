using System.Text.Json;
using OscCommittee.Web.Services.Grading;
using OscCommittee.Web.Services.Tasks;
using Xunit;

namespace OscCommittee.Tests.Services.Grading;

public sealed class GradingScoreTests
{
    [Fact]
    public void TaskPointsAndTheGraderShareOneMaximum()
    {
        Assert.Equal(5, SubmissionScoring.MaxPoints);
        Assert.Equal(SubmissionScoring.MaxPoints, ICSharpTaskService.MaxPoints);
        Assert.Equal(SubmissionScoring.MaxPoints, GradingScore.Maximum);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3.4, 3)]
    [InlineData(3.5, 4)]
    [InlineData(3.6, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    public void ScoresRoundHalfUpAndClamp(double raw, int expected)
    {
        Assert.Equal(expected, GradingScore.Normalize(raw));
    }

    [Fact]
    public void NumericStringsAreAccepted()
    {
        using var document = JsonDocument.Parse("\"4\"");
        var element = document.RootElement.Clone();

        Assert.True(GradingScore.TryRead(element, out var score));
        Assert.Equal(4, score);
    }

    [Fact]
    public void NonNumericScoresAreRejected()
    {
        using var document = JsonDocument.Parse("\"great\"");

        Assert.False(GradingScore.TryRead(document.RootElement, out _));
    }

    [Theory]
    [InlineData("{\"score\":0}", 1)]
    [InlineData("{\"score\":11}", 5)]
    [InlineData("{\"score\":3.6}", 4)]
    [InlineData("{\"score\":\"4\"}", 4)]
    public void ParserNormalisesTheScore(string json, int expected)
    {
        Assert.True(GradingFeedbackParser.TryParse(json, out var feedback, out _));
        Assert.Equal(expected, feedback!.Score);
    }

    [Fact]
    public void ParserRejectsAMissingOrUnusableScore()
    {
        Assert.False(GradingFeedbackParser.TryParse("{\"summary\":\"ok\"}", out _, out var missing));
        Assert.NotNull(missing);

        Assert.False(GradingFeedbackParser.TryParse("{\"score\":\"great\"}", out _, out _));
        Assert.False(GradingFeedbackParser.TryParse("not json", out _, out _));
        Assert.False(GradingFeedbackParser.TryParse("", out _, out _));
    }

    [Fact]
    public void ASyntaxErrorScoresZeroWithoutNeedingAScore()
    {
        const string json = """
            {
              "error": true,
              "summary": "Program.cs line 3: missing semicolon. Fix it and upload again.",
              "strengths": [],
              "issues": [],
              "verdict": "Fix and upload again"
            }
            """;

        Assert.True(GradingFeedbackParser.TryParse(json, out var feedback, out _));
        Assert.Equal(0, feedback!.Score);
        Assert.True(feedback.Error);
    }

    [Fact]
    public void ASyntaxErrorFlagSentAsAStringForcesZero()
    {
        const string json = """
            { "score": 4, "error": "true", "summary": "Broken brace.", "verdict": "Fix" }
            """;

        Assert.True(GradingFeedbackParser.TryParse(json, out var feedback, out _));
        Assert.Equal(0, feedback!.Score);
        Assert.True(feedback.Error);
    }

    [Fact]
    public void AScoreWithoutTheFlagIsANormalAttempt()
    {
        Assert.True(GradingFeedbackParser.TryParse("{\"score\":4}", out var feedback, out _));
        Assert.Equal(4, feedback!.Score);
        Assert.False(feedback.Error);
    }

    [Fact]
    public void StrengthsAndIssuesAreCappedAndOrderedBySeverity()
    {
        const string json = """
            {
              "score": 4,
              "summary": "Solid.",
              "strengths": ["a", "b", "c", "d"],
              "issues": [
                { "severity": "nit", "file": "A.cs", "message": "one" },
                { "severity": "should-fix", "file": "B.cs", "message": "two" },
                { "severity": "must-fix", "file": "C.cs", "message": "three" },
                { "severity": "must-fix", "file": "D.cs", "message": "four" },
                { "severity": "nit", "file": "E.cs", "message": "five" },
                { "severity": "nit", "file": "F.cs", "message": "six" },
                { "severity": "nit", "file": "G.cs", "message": "seven" }
              ],
              "verdict": "Good"
            }
            """;

        Assert.True(GradingFeedbackParser.TryParse(json, out var feedback, out _));

        Assert.Equal(3, feedback!.Strengths.Count);
        Assert.Equal(5, feedback.Issues.Count);
        Assert.Equal(GradingIssueSeverity.MustFix, feedback.Issues[0].Severity);
        Assert.Equal(GradingIssueSeverity.MustFix, feedback.Issues[1].Severity);
        Assert.Equal(GradingIssueSeverity.ShouldFix, feedback.Issues[2].Severity);
        Assert.Equal(GradingIssueSeverity.Nit, feedback.Issues[4].Severity);
    }

    [Theory]
    [InlineData("must-fix", GradingIssueSeverity.MustFix)]
    [InlineData("MustFix", GradingIssueSeverity.MustFix)]
    [InlineData("should-fix", GradingIssueSeverity.ShouldFix)]
    [InlineData("SHOULD FIX", GradingIssueSeverity.ShouldFix)]
    [InlineData("nit", GradingIssueSeverity.Nit)]
    [InlineData("", GradingIssueSeverity.Nit)]
    [InlineData(null, GradingIssueSeverity.Nit)]
    public void SeverityWordingIsBucketed(string? raw, GradingIssueSeverity expected)
    {
        Assert.Equal(expected, GradingFeedbackParser.ParseSeverity(raw));
    }

    [Fact]
    public void EmptyIssueMessagesAreDropped()
    {
        const string json = """
            {
              "score": 5,
              "issues": [
                { "severity": "must-fix", "file": "A.cs", "message": "  " },
                { "severity": "nit", "file": "B.cs", "message": "keep" }
              ]
            }
            """;

        Assert.True(GradingFeedbackParser.TryParse(json, out var feedback, out _));
        Assert.Single(feedback!.Issues);
        Assert.Equal("keep", feedback.Issues[0].Message);
    }

    [Fact]
    public void FeedbackRoundTripsThroughCanonicalJson()
    {
        var feedback = new GradingFeedback(
            5,
            "Works.",
            ["clear"],
            [new GradingIssue(GradingIssueSeverity.MustFix, "Program.cs", "fix this")],
            "Good");

        var json = GradingFeedbackJson.Serialize(feedback);
        var restored = GradingFeedbackJson.Deserialize(json);

        Assert.Contains("\"must-fix\"", json);
        Assert.NotNull(restored);
        Assert.Equal(feedback.Score, restored!.Score);
        Assert.Equal(feedback.Summary, restored.Summary);
        Assert.Equal(feedback.Strengths, restored.Strengths);
        Assert.Equal(feedback.Issues, restored.Issues);
        Assert.Equal(feedback.Verdict, restored.Verdict);
        Assert.Null(GradingFeedbackJson.Deserialize("not json"));
    }
}