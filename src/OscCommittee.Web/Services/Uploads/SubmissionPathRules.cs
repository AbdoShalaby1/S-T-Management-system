using System.Text.RegularExpressions;

namespace OscCommittee.Web.Services.Uploads;

public enum SubmissionPathIssue
{
    None = 0,
    Empty,
    EscapesRoot,
    TooDeep,
    TooLong,
    InvalidCharacter
}

/// <summary>Outcome of normalising one browser-supplied relative path (PLAN §10.4).</summary>
public sealed record NormalizedSubmissionPath(bool Succeeded, string Path, SubmissionPathIssue Issue)
{
    public static NormalizedSubmissionPath Ok(string path) => new(true, path, SubmissionPathIssue.None);

    public static NormalizedSubmissionPath Fail(SubmissionPathIssue issue) => new(false, string.Empty, issue);
}

/// <summary>
/// Pure path rules shared by the planner, the storage writer and the tests. Nothing here touches
/// the file system; storage still resolves every path under the upload root as defence in depth.
/// </summary>
public static partial class SubmissionPathRules
{
    public const int MaxDepth = 20;

    public const int MaxPathLength = 260;

    /// <summary>Build output and editor caches are never stored or graded.</summary>
    public static readonly IReadOnlySet<string> ExcludedDirectories =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", ".vs", "node_modules", ".idea"
        };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>
    /// Replaces <c>\</c> with <c>/</c>, strips a drive letter and leading slashes, drops empty and
    /// <c>.</c> segments, and rejects <c>..</c>, over-deep, over-long and colon-bearing paths.
    /// </summary>
    public static NormalizedSubmissionPath Normalize(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return NormalizedSubmissionPath.Fail(SubmissionPathIssue.Empty);
        }

        var value = rawPath.Trim().Replace('\\', '/');

        // A browser sending a full Windows path (drag-and-drop in some shells) starts with a drive.
        value = DrivePrefix().Replace(value, string.Empty);
        value = value.TrimStart('/');

        var segments = new List<string>();
        foreach (var segment in value.Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                return NormalizedSubmissionPath.Fail(SubmissionPathIssue.EscapesRoot);
            }

            // ':' would smuggle an alternate data stream or a drive onto Windows, and the rest are
            // illegal in a Windows file name. A trailing dot or space silently normalises away.
            if (segment.IndexOfAny(InvalidCharacters) >= 0
                || segment.Any(char.IsControl)
                || segment.EndsWith('.')
                || segment.EndsWith(' ')
                || ReservedNames.Contains(segment.Split('.')[0]))
            {
                return NormalizedSubmissionPath.Fail(SubmissionPathIssue.InvalidCharacter);
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return NormalizedSubmissionPath.Fail(SubmissionPathIssue.Empty);
        }

        if (segments.Count > MaxDepth)
        {
            return NormalizedSubmissionPath.Fail(SubmissionPathIssue.TooDeep);
        }

        var normalized = string.Join('/', segments);
        return normalized.Length > MaxPathLength
            ? NormalizedSubmissionPath.Fail(SubmissionPathIssue.TooLong)
            : NormalizedSubmissionPath.Ok(normalized);
    }

    /// <summary>True when a non-final segment names a build-output or editor directory.</summary>
    public static bool IsInExcludedDirectory(string normalizedPath)
    {
        var segments = normalizedPath.Split('/');
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (ExcludedDirectories.Contains(segments[index]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Case-insensitive allowlist: extensions, exact/glob names and glob patterns.</summary>
    public static bool IsAllowed(string normalizedPath, UploadsOptions options)
    {
        var fileName = FileName(normalizedPath);
        var extension = Path.GetExtension(fileName);

        return options.AllowedExtensions.Any(allowed =>
                   string.Equals(allowed, extension, StringComparison.OrdinalIgnoreCase))
               || options.AllowedFileNames.Any(pattern => GlobMatch(pattern, fileName))
               || options.AllowedNamePatterns.Any(pattern => GlobMatch(pattern, fileName));
    }

    /// <summary>Leaf file name, e.g. <c>Program.cs</c> for <c>src/Program.cs</c>.</summary>
    public static string FileName(string normalizedPath)
    {
        var slash = normalizedPath.LastIndexOf('/');
        return slash < 0 ? normalizedPath : normalizedPath[(slash + 1)..];
    }

    /// <summary>
    /// The folder picker sends the selected folder name as the first segment. When every kept
    /// path shares that segment, strip it so the bundle tree starts at the project root.
    /// </summary>
    public static IReadOnlyList<string> StripCommonRoot(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return paths;
        }

        var roots = paths.Select(path => path.Split('/')).ToList();
        if (roots.Any(segments => segments.Length < 2))
        {
            return paths;
        }

        var root = roots[0][0];
        if (!roots.All(segments => string.Equals(segments[0], root, StringComparison.OrdinalIgnoreCase)))
        {
            return paths;
        }

        return paths
            .Select(path => path[(path.IndexOf('/') + 1)..])
            .ToList();
    }

    /// <summary>
    /// Resolves a normalized relative path under <paramref name="root"/> and refuses anything that
    /// would land outside it. Returns null when the combined path escapes the root.
    /// </summary>
    public static string? ResolveUnderRoot(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var combined = Path.GetFullPath(
            Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    /// <summary>Whole-name glob with <c>*</c> as the only wildcard; case-insensitive.</summary>
    public static bool GlobMatch(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex("^[A-Za-z]:")]
    private static partial Regex DrivePrefix();
}