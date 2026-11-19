using OscCommittee.Web.Services.Uploads;
using Xunit;

namespace OscCommittee.Tests.Services.Uploads;

public sealed class SubmissionPathRulesTests
{
    private static readonly UploadsOptions Options = new();

    [Theory]
    [InlineData(@"C:\project\Program.cs", "project/Program.cs")]
    [InlineData(@"\project\src\Program.cs", "project/src/Program.cs")]
    [InlineData("project/./src/Program.cs", "project/src/Program.cs")]
    [InlineData("project//src///Program.cs", "project/src/Program.cs")]
    public void PathsAreNormalisedToForwardSlashes(string raw, string expected)
    {
        var result = SubmissionPathRules.Normalize(raw);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Path);
    }

    [Theory]
    [InlineData("project/../Program.cs")]
    [InlineData("../Program.cs")]
    [InlineData(@"project\..\Program.cs")]
    public void ParentSegmentsAreRejected(string raw)
    {
        var result = SubmissionPathRules.Normalize(raw);

        Assert.False(result.Succeeded);
        Assert.Equal(SubmissionPathIssue.EscapesRoot, result.Issue);
    }

    [Fact]
    public void DeepAndLongPathsAreRejectedRatherThanSilentlyTruncated()
    {
        var deep = string.Join('/', Enumerable.Repeat("folder", 21)) + "/Program.cs";
        var longPath = new string('a', 250) + "/Program.cs";

        Assert.Equal(SubmissionPathIssue.TooDeep, SubmissionPathRules.Normalize(deep).Issue);
        Assert.Equal(SubmissionPathIssue.TooLong, SubmissionPathRules.Normalize(longPath).Issue);
    }

    [Theory]
    [InlineData("project/CON.cs")]
    [InlineData("project/nul")]
    [InlineData("project/name./file.cs")]
    [InlineData("project/name /file.cs")]
    [InlineData("project/a?.cs")]
    [InlineData("project/a|b.cs")]
    public void WindowsHostileSegmentsAreRejected(string raw)
    {
        var result = SubmissionPathRules.Normalize(raw);

        Assert.False(result.Succeeded);
        Assert.Equal(SubmissionPathIssue.InvalidCharacter, result.Issue);
    }

    [Theory]
    [InlineData("src/bin/Debug/App.dll")]
    [InlineData("project/obj/Release/Program.cs")]
    [InlineData(".git/config")]
    [InlineData("project/.vs/state.json")]
    [InlineData("web/node_modules/left-pad/index.js")]
    [InlineData("project/.idea/workspace.xml")]
    public void BuildOutputAndEditorFoldersAreExcluded(string path)
    {
        Assert.True(SubmissionPathRules.IsInExcludedDirectory(path));
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("project/bin")]
    public void AFileNamedLikeAnExcludedFolderIsNotExcluded(string path)
    {
        Assert.False(SubmissionPathRules.IsInExcludedDirectory(path));
    }

    [Theory]
    [InlineData("Program.cs", true)]
    [InlineData("Project.csproj", true)]
    [InlineData("Solution.sln", true)]
    [InlineData("appsettings.json", true)]
    [InlineData("appsettings.Development.json", true)]
    [InlineData("APPSETTINGS.PRODUCTION.JSON", true)]
    [InlineData("README", true)]
    [InlineData("README.md", true)]
    [InlineData("readme.txt", true)]
    [InlineData("notes.txt", false)]
    [InlineData("appsettings.json.bak", false)]
    [InlineData("Program.cs.bak", false)]
    public void AllowlistMatchesCaseInsensitively(string path, bool allowed)
    {
        Assert.Equal(allowed, SubmissionPathRules.IsAllowed(path, Options));
    }

    [Fact]
    public void TheSharedFolderNameIsStrippedFromEveryPath()
    {
        var stripped = SubmissionPathRules.StripCommonRoot(
            ["MyProject/Program.cs", "MyProject/src/Helper.cs"]);

        Assert.Equal(new[] { "Program.cs", "src/Helper.cs" }, stripped);
    }

    [Fact]
    public void MixedRootsAreKeptIntact()
    {
        var paths = new[] { "One/Program.cs", "Two/Program.cs" };

        Assert.Equal(paths, SubmissionPathRules.StripCommonRoot(paths));
    }

    [Fact]
    public void ResolveUnderRootRefusesEscapes()
    {
        var root = Path.Combine(Path.GetTempPath(), "osc-upload-root");

        Assert.NotNull(SubmissionPathRules.ResolveUnderRoot(root, "project/Program.cs"));
        Assert.Null(SubmissionPathRules.ResolveUnderRoot(root, "../outside.cs"));
    }

    [Fact]
    public void PlannerKeepsTheLastDuplicateAndReportsIt()
    {
        var first = new SubmissionInputFile(
            "project/Program.cs",
            10,
            _ => Task.FromResult<Stream>(new MemoryStream()));
        var second = new SubmissionInputFile(
            "project/Program.cs",
            20,
            _ => Task.FromResult<Stream>(new MemoryStream()));

        var plan = SubmissionPlanner.Plan([first, second], Options);

        Assert.True(plan.Succeeded);
        Assert.Single(plan.Kept);
        Assert.Equal(second.OpenAsync, plan.Kept[0].OpenAsync);
        Assert.Equal(1, plan.DuplicateCount);
        Assert.Equal(20, plan.TotalBytes);
    }

    [Fact]
    public void PlannerIgnoresBuildOutputAndReportsTheCount()
    {
        var plan = SubmissionPlanner.Plan(
            [
                new("MyProject/obj/Debug/App.dll", 100, EmptyStream),
                new("MyProject/notes.txt", 50, EmptyStream),
                new("MyProject/Program.cs", 200, EmptyStream)
            ],
            Options);

        Assert.True(plan.Succeeded);
        Assert.Single(plan.Kept);
        Assert.Equal("Program.cs", plan.Kept[0].Path);
        Assert.Equal(2, plan.IgnoredCount);
        Assert.Equal(2, plan.NotKeptCount);
    }

    [Fact]
    public void PlannerRejectsFoldersWithoutCode()
    {
        var plan = SubmissionPlanner.Plan(
            [
                new("MyProject/obj/Debug/App.dll", 100, EmptyStream),
                new("MyProject/README.md", 50, EmptyStream)
            ],
            Options);

        Assert.False(plan.Succeeded);
        Assert.Equal("no_code_files", plan.FailureCode);
        Assert.Contains("no .cs files", plan.FailureMessage!);
    }

    [Fact]
    public void PlannerRejectsPathsThatEscapeTheRoot()
    {
        var plan = SubmissionPlanner.Plan(
            [new("../Program.cs", 10, EmptyStream)],
            Options);

        Assert.False(plan.Succeeded);
        Assert.Equal("path_escape", plan.FailureCode);
    }

    [Fact]
    public void PlannerEnforcesCountPerFileAndTotalLimits()
    {
        var tooMany = Enumerable.Range(0, Options.MaxFiles + 1)
            .Select(index => new SubmissionInputFile($"f{index}.cs", 1, EmptyStream))
            .ToList();

        Assert.Equal("too_many_files", SubmissionPlanner.Plan(tooMany, Options).FailureCode);

        var oneBig = new SubmissionInputFile(
            "Program.cs",
            Options.MaxFileBytes + 1,
            EmptyStream);
        Assert.Equal("file_too_large", SubmissionPlanner.Plan([oneBig], Options).FailureCode);

        var totalTooBig = Enumerable.Range(0, 20)
            .Select(index => new SubmissionInputFile(
                $"f{index}.cs",
                Options.MaxFileBytes,
                EmptyStream))
            .ToList();
        Assert.Equal("total_too_large", SubmissionPlanner.Plan(totalTooBig, Options).FailureCode);
    }

    [Fact]
    public void EmptyUploadsAreRejectedWithCopy()
    {
        var plan = SubmissionPlanner.Plan([], Options);

        Assert.False(plan.Succeeded);
        Assert.Equal("no_files", plan.FailureCode);
        Assert.Equal(UploadCopy.NoFiles, plan.FailureMessage);
    }

    private static Task<Stream> EmptyStream(CancellationToken _) =>
        Task.FromResult<Stream>(new MemoryStream());
}