using System.Globalization;
using System.Text;

namespace OscCommittee.Web.Services.Uploads;

/// <summary>One text file as it will appear in the bundle.</summary>
public sealed record BundleFile(string Path, string Content);

/// <summary>Everything the manifest and the per-file sections need (PLAN §22.2).</summary>
public sealed record BundleInput(
    string TaskTitle,
    string MemberName,
    string MonthLabel,
    string SubmittedLabel,
    string TimeZoneId,
    int AttemptNo,
    bool IsLate,
    int NotKeptFileCount,
    long TotalBytes,
    IReadOnlyList<BundleFile> Files);

public sealed record BundleResult(
    string Markdown,
    int Chars,
    int KeptFiles,
    long TotalBytes);

/// <summary>Builds the Markdown document sent to the grader (pure, no IO).</summary>
public interface IMarkdownBundleService
{
    BundleResult Build(BundleInput input);
}

/// <summary>
/// Implements PLAN §22.2: a manifest header, a file tree, then one four-backtick fenced section per
/// file. The whole filtered submission is sent; no input cap is applied.
/// </summary>
public sealed class MarkdownBundleService : IMarkdownBundleService
{
    public BundleResult Build(BundleInput input)
    {
        // Files are always presented in path order, whatever order the upload arrived in.
        var files = input.Files
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToList();

        var contents = files.Select(file => Normalize(file.Content)).ToList();
        var markdown = Render(input, files, contents);

        return new BundleResult(markdown, markdown.Length, files.Count, input.TotalBytes);
    }

    private static string Render(
        BundleInput input,
        IReadOnlyList<BundleFile> files,
        IReadOnlyList<string> contents)
    {
        var builder = new StringBuilder();
        var notKept = Math.Max(0, input.NotKeptFileCount);

        builder.Append("# Submission: ").AppendLine(input.TaskTitle);
        builder.AppendLine();
        builder.Append("- Member: ").AppendLine(input.MemberName);
        builder.Append("- Task: ").Append(input.TaskTitle)
            .Append(" (").Append(input.MonthLabel).AppendLine(")");
        builder.Append("- Submitted: ").Append(input.SubmittedLabel)
            .Append(" (").Append(input.TimeZoneId).AppendLine(")");
        builder.Append("- Attempt: ").Append(input.AttemptNo.ToString(CultureInfo.InvariantCulture)).AppendLine();
        builder.Append("- Late: ").AppendLine(input.IsLate ? "yes" : "no");
        builder.Append("- Files kept: ").Append(files.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" (").Append(notKept.ToString(CultureInfo.InvariantCulture)).AppendLine(" ignored)");
        builder.Append("- Total size: ").AppendLine(FormatBytes(input.TotalBytes));
        builder.AppendLine();

        builder.AppendLine("## File tree");
        builder.AppendLine();
        builder.AppendLine("```text");
        foreach (var file in files)
        {
            builder.Append('/').AppendLine(file.Path);
        }

        builder.AppendLine("```");

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var content = contents[index];

            builder.AppendLine();
            builder.Append("## /").AppendLine(file.Path);
            builder.AppendLine();
            builder.Append("````").Append(Language(file.Path)).AppendLine();
            builder.Append(content);
            if (content.Length > 0 && content[^1] != '\n')
            {
                builder.AppendLine();
            }

            builder.AppendLine("````");
        }

        // One newline style everywhere keeps bundle char counts identical on every OS.
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>A code fence language for the file; unknown files stay plain text.</summary>
    public static string Language(string path)
    {
        var fileName = SubmissionPathRules.FileName(path);
        var extension = Path.GetExtension(fileName);

        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".json" => "json",
            ".csproj" => "xml",
            ".xml" => "xml",
            ".md" => "markdown",
            _ => fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase) ? "markdown" : "text"
        };
    }

    public static string FormatBytes(long bytes)
    {
        const double kilo = 1024;
        const double mega = kilo * 1024;

        return bytes >= mega
            ? $"{bytes / mega:0.0} MB"
            : $"{bytes / kilo:0.0} KB";
    }

    private static string Normalize(string? content) =>
        string.IsNullOrEmpty(content)
            ? string.Empty
            : content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd('\n');
}
