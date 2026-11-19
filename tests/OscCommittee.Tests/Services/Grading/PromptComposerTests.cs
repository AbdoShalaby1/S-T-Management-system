using OscCommittee.Web.Services.Grading;
using Xunit;

namespace OscCommittee.Tests.Services.Grading;

public sealed class PromptComposerTests
{
    private static PromptRequest Request(string bundle = "## /Program.cs\n\n````csharp\nclass A { }\n````") => new(
        "Loops",
        "November 2026",
        "## Goal\n\nPrint the numbers.",
        "### Reference.cs\n\n````\nclass A { }\n````",
        bundle);

    [Fact]
    public void ThePromptCarriesEveryInputAndTheDataNotice()
    {
        var result = PromptComposer.Compose("You are grading.", 3, "Loops and LINQ.", Request());

        Assert.Contains("You are grading.", result.Text);
        Assert.Contains("=== TASK ===", result.Text);
        Assert.Contains("Print the numbers.", result.Text);
        Assert.Contains("Loops and LINQ.", result.Text);
        Assert.Contains("Reference.cs", result.Text);
        Assert.Contains("class A { }", result.Text);
        Assert.Contains(PromptComposer.DataNotice, result.Text);
        Assert.Contains("<<<SUBMISSION", result.Text);
        Assert.Contains("SUBMISSION>>>", result.Text);
        Assert.Equal(3, result.Version);
        Assert.Equal(result.Text.Length, result.InputChars);
        Assert.Equal(PromptComposer.Hash(result.Text), result.Hash);
    }

    [Fact]
    public void MissingOptionalInputsAreStatedPlainly()
    {
        var result = PromptComposer.Compose(
            "You are grading.",
            1,
            null,
            new PromptRequest("Task", "November 2026", null, null, "## /Program.cs\n````csharp\nclass A { }\n````"));

        Assert.Contains("Not provided.", result.Text);
        Assert.DoesNotContain("<<<SUBMISSION\n\nSUBMISSION>>>", result.Text);
    }

    [Fact]
    public void TheHashIsStableAndChangesWithTheContent()
    {
        var first = PromptComposer.Compose("prompt", 1, "", Request());
        var same = PromptComposer.Compose("prompt", 1, "", Request());
        var different = PromptComposer.Compose("prompt", 1, "", Request("## /Other.cs\n````csharp\nclass B { }\n````"));

        Assert.Equal(first.Hash, same.Hash);
        Assert.NotEqual(first.Hash, different.Hash);
        Assert.Equal(64, first.Hash.Length);
    }

    [Fact]
    public void SecretsAreRedactedBeforeTheBundleIsEmbedded()
    {
        const string bundle = """
            ## /appsettings.json

            ````json
            { "Password": "hunter2" }
            ````
            """;

        var result = PromptComposer.Compose("prompt", 1, "", Request(bundle));

        Assert.DoesNotContain("hunter2", result.Text);
        Assert.Contains("***", result.Text);
    }
}