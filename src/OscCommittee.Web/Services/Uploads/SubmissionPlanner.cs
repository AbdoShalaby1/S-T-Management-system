namespace OscCommittee.Web.Services.Uploads;

/// <summary>One file exactly as the browser sent it: a relative path, a length and a stream opener.</summary>
public sealed record SubmissionInputFile(
    string RelativePath,
    long Length,
    Func<CancellationToken, Task<Stream>> OpenAsync);

/// <summary>A file that survived path normalisation, filtering and duplicate collapsing.</summary>
public sealed record PlannedSubmissionFile(
    string Path,
    long Length,
    Func<CancellationToken, Task<Stream>> OpenAsync);

/// <summary>The accepted file set plus the counts shown in the bundle and the toast.</summary>
public sealed class SubmissionPlan
{
    public static readonly SubmissionPlan Empty = new([], 0, 0, 0, "no_files", UploadCopy.NoFiles);

    private SubmissionPlan(
        IReadOnlyList<PlannedSubmissionFile> kept,
        int ignoredCount,
        int duplicateCount,
        long totalBytes,
        string? failureCode,
        string? failureMessage)
    {
        Kept = kept;
        IgnoredCount = ignoredCount;
        DuplicateCount = duplicateCount;
        TotalBytes = totalBytes;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }

    public IReadOnlyList<PlannedSubmissionFile> Kept { get; }

    /// <summary>Unique files dropped by the directory or extension filters.</summary>
    public int IgnoredCount { get; }

    /// <summary>Extra copies of a path that was already seen; the last copy wins.</summary>
    public int DuplicateCount { get; }

    /// <summary>Everything sent but not kept, exactly what the bundle manifest reports.</summary>
    public int NotKeptCount => IgnoredCount + DuplicateCount;

    /// <summary>Kept bytes only, so build output never inflates the reported size.</summary>
    public long TotalBytes { get; }

    public string? FailureCode { get; }

    public string? FailureMessage { get; }

    public bool Succeeded => FailureCode is null;

    public static SubmissionPlan Fail(string code, string message) => new([], 0, 0, 0, code, message);

    public static SubmissionPlan Success(
        IReadOnlyList<PlannedSubmissionFile> kept,
        int ignoredCount,
        int duplicateCount,
        long totalBytes) =>
        new(kept, ignoredCount, duplicateCount, totalBytes, null, null);
}

/// <summary>Error copy from PLAN §14.7: say what happened and what to do next, never shame.</summary>
public static class UploadCopy
{
    public const string NoFiles = "Your upload had no files. Pick the project folder and try again.";

    public const string NoCodeFiles =
        "Your folder had no .cs files. Add at least one .cs file and upload again.";

    public const string PathEscape =
        "A file path points outside the folder you selected. Re-select the project folder and try again.";

    public const string PathInvalid =
        "A file path is too long or uses characters we cannot store. Move the project up a folder and upload again.";

    public static string TooManyFiles(int max) =>
        $"That folder has more than {max} files. Upload the project folder without build output (bin, obj) and try again.";

    public static string FileTooLarge(string fileName, int maxMb) =>
        $"“{fileName}” is over {maxMb} MB. Remove build output (bin, obj) and try again.";

    public static string TotalTooLarge(int maxMb) =>
        $"That upload is over {maxMb} MB. Remove build output (bin, obj) and try again.";
}

/// <summary>
/// Pure planning step of the upload pipeline (PLAN §10.4 steps 1–5): validate, normalise, filter,
/// collapse duplicates, require code, and strip the folder name the picker included.
/// </summary>
public static class SubmissionPlanner
{
    public static SubmissionPlan Plan(
        IReadOnlyList<SubmissionInputFile> files,
        UploadsOptions options,
        bool requireCode = true)
    {
        if (files.Count == 0)
        {
            return SubmissionPlan.Fail("no_files", UploadCopy.NoFiles);
        }

        if (files.Count > options.MaxFiles)
        {
            return SubmissionPlan.Fail("too_many_files", UploadCopy.TooManyFiles(options.MaxFiles));
        }

        var oversized = files.FirstOrDefault(file => file.Length > options.MaxFileBytes);
        if (oversized is not null)
        {
            return SubmissionPlan.Fail(
                "file_too_large",
                UploadCopy.FileTooLarge(
                    SubmissionPathRules.FileName(oversized.RelativePath.Replace('\\', '/')),
                    options.MaxFileMb));
        }

        if (files.Sum(file => file.Length) > options.MaxTotalBytes)
        {
            return SubmissionPlan.Fail("total_too_large", UploadCopy.TotalTooLarge(options.MaxTotalMb));
        }

        var unique = new Dictionary<string, SubmissionInputFile>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;

        foreach (var file in files)
        {
            var normalized = SubmissionPathRules.Normalize(file.RelativePath);
            if (!normalized.Succeeded)
            {
                var (code, message) = normalized.Issue == SubmissionPathIssue.EscapesRoot
                    ? ("path_escape", UploadCopy.PathEscape)
                    : ("path_invalid", UploadCopy.PathInvalid);

                return SubmissionPlan.Fail(code, message);
            }

            // Duplicate paths are allowed: the last copy wins, matching §16.
            if (unique.ContainsKey(normalized.Path))
            {
                duplicates++;
            }

            unique[normalized.Path] = file with { RelativePath = normalized.Path };
        }

        var ignored = 0;
        var kept = new List<(string Path, SubmissionInputFile Source)>();

        foreach (var (path, source) in unique)
        {
            if (SubmissionPathRules.IsInExcludedDirectory(path)
                || !SubmissionPathRules.IsAllowed(path, options))
            {
                ignored++;
                continue;
            }

            kept.Add((path, source));
        }

        if (!kept.Any(entry => string.Equals(
                Path.GetExtension(entry.Path),
                ".cs",
                StringComparison.OrdinalIgnoreCase)))
        {
            if (requireCode)
            {
                return SubmissionPlan.Fail("no_code_files", UploadCopy.NoCodeFiles);
            }

            if (kept.Count == 0)
            {
                return SubmissionPlan.Fail("no_files", UploadCopy.NoFiles);
            }
        }

        // The folder picker prefixes every path with the selected folder; the project root is inside.
        var stripped = SubmissionPathRules.StripCommonRoot(kept.Select(entry => entry.Path).ToList());

        var planned = stripped
            .Select((path, index) => new PlannedSubmissionFile(
                path,
                kept[index].Source.Length,
                kept[index].Source.OpenAsync))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToList();

        return SubmissionPlan.Success(
            planned,
            ignored,
            duplicates,
            planned.Sum(file => file.Length));
    }
}