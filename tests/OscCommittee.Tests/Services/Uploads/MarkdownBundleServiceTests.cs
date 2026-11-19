using OscCommittee.Web.Services.Uploads;
using Xunit;

namespace OscCommittee.Tests.Services.Uploads;

public sealed class MarkdownBundleServiceTests
{
    private readonly MarkdownBundleService _bundles = new();

    private static BundleInput Input(
        IReadOnlyList<BundleFile> files,
        int ignored = 3,
        long totalBytes = 42_000) => new(
        "Loops & LINQ",
        "Yara Hassan",
        "November 2026",
        "2026-11-16 21:04",
        "Africa/Cairo",
        2,
        false,
        ignored,
        totalBytes,
        files);

    [Fact]
    public void TheManifestMatchesPlan22()
    {
        var result = _bundles.Build(Input(
            [
                new BundleFile("Program.cs", "Console.WriteLine(1);"),
                new BundleFile("appsettings.json", "{}")
            ]));

        Assert.Contains("# Submission: Loops & LINQ", result.Markdown);
        Assert.Contains("- Member: Yara Hassan", result.Markdown);
        Assert.Contains("- Task: Loops & LINQ (November 2026)", result.Markdown);
        Assert.Contains("- Submitted: 2026-11-16 21:04 (Africa/Cairo)", result.Markdown);
        Assert.Contains("- Attempt: 2", result.Markdown);
        Assert.Contains("- Late: no", result.Markdown);
        Assert.Contains("- Files kept: 2 (3 ignored)", result.Markdown);
        Assert.Contains("- Total size: 41.0 KB", result.Markdown);
        Assert.Equal(2, result.KeptFiles);
        Assert.Equal(42_000, result.TotalBytes);
    }

    [Fact]
    public void FilesAreOrderedByPathAndListedInTheTree()
    {
        var result = _bundles.Build(Input(
            [
                new BundleFile("Zeta.cs", "class Zeta {}"),
                new BundleFile("Alpha.cs", "class Alpha {}")
            ]));

        var alphaSection = result.Markdown.IndexOf("## /Alpha.cs", StringComparison.Ordinal);
        var zetaSection = result.Markdown.IndexOf("## /Zeta.cs", StringComparison.Ordinal);
        var alphaTree = result.Markdown.IndexOf("/Alpha.cs", StringComparison.Ordinal);

        Assert.True(alphaSection >= 0);
        Assert.True(zetaSection > alphaSection);
        Assert.True(alphaTree < alphaSection);
    }

    [Fact]
    public void SectionsUseFourBackticksSoCSharpFencesCannotBreakTheDocument()
    {
        var result = _bundles.Build(Input(
            [new BundleFile("Program.cs", "var fence = \"```\";\nConsole.WriteLine(fence);")]));

        Assert.Contains("````csharp", result.Markdown);
        Assert.Contains("var fence = \"```\";", result.Markdown);
        Assert.Contains("```text", result.Markdown);
    }

    [Theory]
    [InlineData("Program.cs", "csharp")]
    [InlineData("Data/AppDbContext.cs", "csharp")]
    [InlineData("appsettings.json", "json")]
    [InlineData("Project.csproj", "xml")]
    [InlineData("Solution.sln", "text")]
    [InlineData("README", "markdown")]
    [InlineData("README.md", "markdown")]
    public void FenceLanguagesFollowTheFileType(string path, string language)
    {
        Assert.Equal(language, MarkdownBundleService.Language(path));
    }

    [Fact]
    public void LineEndingsAreNormalised()
    {
        var result = _bundles.Build(Input(
            [new BundleFile("Program.cs", "class A\r\n{\r\n}")]));

        Assert.DoesNotContain("\r", result.Markdown);
        Assert.Contains("class A\n{\n}", result.Markdown);
    }

    [Fact]
    public void TheWholeSubmissionIsSentWithoutTruncation()
    {
        var big = new string('x', 400_000);
        var small = "class Small {}";

        var result = _bundles.Build(Input(
            [
                new BundleFile("Big.cs", big),
                new BundleFile("Small.cs", small)
            ]));

        Assert.Contains(big, result.Markdown);
        Assert.Contains(small, result.Markdown);
        Assert.DoesNotContain("<!-- truncated:", result.Markdown);
        Assert.Equal(2, result.KeptFiles);
    }

    [Fact]
    public void AnEmptyFileStillGetsItsSection()
    {
        var result = _bundles.Build(Input([new BundleFile("Empty.cs", string.Empty)]));

        Assert.Contains("## /Empty.cs", result.Markdown);
        Assert.Contains("````csharp", result.Markdown);
    }
}
