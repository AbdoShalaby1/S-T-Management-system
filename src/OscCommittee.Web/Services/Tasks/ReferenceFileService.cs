using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Web.Services.Tasks;

/// <summary>The stored reference folder after a successful upload.</summary>
public sealed record ReferenceUpload(
    string FolderPath,
    int FileCount,
    int IgnoredCount,
    long TotalBytes);

/// <summary>
/// Stores the board's optional reference (a folder of files or a single file) under
/// <c>{Uploads:Root}/references/{guid}</c>. It reuses the member upload pipeline for path
/// normalisation, filtering and limits, but a reference keeps any allowed text file, not only
/// <c>.cs</c>. Reads are by the stored relative path only.
/// </summary>
public interface IReferenceFileService
{
    Task<Result<ReferenceUpload>> SaveAsync(
        IReadOnlyList<SubmissionInputFile> files,
        CancellationToken cancellationToken = default);

    Task<SubmissionFilesView?> ReadAsync(
        string? folderPath,
        string? selectedPath,
        CancellationToken cancellationToken = default);

    Task<Result<SubmissionArchive>> BuildArchiveAsync(
        string folderPath,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the reference as one Markdown block for the grading prompt, capped by length.</summary>
    Task<string?> ReadPromptTextAsync(
        string? folderPath,
        int maxChars,
        CancellationToken cancellationToken = default);

    /// <summary>Best-effort removal of a replaced reference folder. Never throws.</summary>
    void Delete(string? folderPath);
}

public sealed class ReferenceFileService(
    IWebHostEnvironment environment,
    IOptions<UploadsOptions> uploads,
    ILogger<ReferenceFileService> logger) : IReferenceFileService
{
    private const int MaxDisplayChars = 120_000;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<Result<ReferenceUpload>> SaveAsync(
        IReadOnlyList<SubmissionInputFile> files,
        CancellationToken cancellationToken = default)
    {
        var plan = SubmissionPlanner.Plan(files, uploads.Value, requireCode: false);
        if (!plan.Succeeded)
        {
            return Result.Fail<ReferenceUpload>(
                plan.FailureCode ?? "invalid_upload",
                plan.FailureMessage ?? UploadCopy.NoFiles);
        }

        var folderRelative = $"references/{Guid.CreateVersion7():N}";
        var baseFolder = ResolveFolder(folderRelative);
        if (baseFolder is null)
        {
            return Result.Fail<ReferenceUpload>("path_escape", UploadCopy.PathEscape);
        }

        var filesFolder = Path.Combine(baseFolder, "files");
        var saved = false;

        try
        {
            Directory.CreateDirectory(filesFolder);

            foreach (var file in plan.Kept)
            {
                var target = SubmissionPathRules.ResolveUnderRoot(filesFolder, file.Path);
                if (target is null)
                {
                    return Result.Fail<ReferenceUpload>("path_escape", UploadCopy.PathEscape);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var stream = await file.OpenAsync(cancellationToken);
                await using var destination = File.Create(target);
                await stream.CopyToAsync(destination, cancellationToken);
            }

            saved = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Storing a reference folder failed.");
            return Result.Fail<ReferenceUpload>(
                "storage_failed",
                "The solution could not be saved. Try again; if it keeps failing, tell the board.");
        }
        finally
        {
            if (!saved)
            {
                Delete(folderRelative);
            }
        }

        return Result.Ok(
            new ReferenceUpload(folderRelative, plan.Kept.Count, plan.NotKeptCount, plan.TotalBytes),
            "Solution saved.");
    }

    public async Task<SubmissionFilesView?> ReadAsync(
        string? folderPath,
        string? selectedPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        var filesRoot = ResolveFilesRoot(folderPath);
        if (filesRoot is null || !Directory.Exists(filesRoot))
        {
            return new SubmissionFilesView([], null, null, "csharp", false,
                "The stored solution files are missing. Upload the solution again.");
        }

        var files = ListFiles(filesRoot);
        if (files.Count == 0)
        {
            return new SubmissionFilesView([], null, null, "csharp", false,
                "This solution has no stored files.");
        }

        var path = ResolveRequestedPath(filesRoot, selectedPath)
                   ?? files.FirstOrDefault(file => file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))?.Path
                   ?? files[0].Path;

        var fullPath = SubmissionPathRules.ResolveUnderRoot(filesRoot, path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return new SubmissionFilesView(files, null, null, "csharp", false,
                "That file is not part of this solution.");
        }

        string content;
        try
        {
            content = await ReadTextAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Reading reference file {Path} failed.", fullPath);
            return new SubmissionFilesView(files, path, null, SubmissionFileService.LanguageFor(path), false,
                "That file could not be read from disk.");
        }

        var truncated = content.Length > MaxDisplayChars;
        if (truncated)
        {
            content = content[..MaxDisplayChars] +
                      "\n\n// Display truncated. Download the solution to read the whole file.";
        }

        return new SubmissionFilesView(
            files,
            path,
            content,
            SubmissionFileService.LanguageFor(path),
            truncated,
            null);
    }

    public async Task<Result<SubmissionArchive>> BuildArchiveAsync(
        string folderPath,
        string name,
        CancellationToken cancellationToken = default)
    {
        var filesRoot = ResolveFilesRoot(folderPath);
        if (filesRoot is null || !Directory.Exists(filesRoot))
        {
            return Result.Fail<SubmissionArchive>(
                "missing_files",
                "The stored solution files are missing, so there is nothing to download.");
        }

        var files = ListFiles(filesRoot);
        if (files.Count == 0)
        {
            return Result.Fail<SubmissionArchive>("empty", "This solution has no stored files.");
        }

        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var fullPath = SubmissionPathRules.ResolveUnderRoot(filesRoot, file.Path);
                if (fullPath is null || !File.Exists(fullPath))
                {
                    continue;
                }

                var entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var source = File.OpenRead(fullPath);
                await source.CopyToAsync(entryStream, cancellationToken);
            }
        }

        var clean = CleanFileName(name);
        return Result.Ok(new SubmissionArchive(buffer.ToArray(), clean + ".zip"));
    }

    /// <summary>
    /// Reads the reference as one Markdown block for the grading prompt. Capped so a huge folder
    /// cannot blow up the model input.
    /// </summary>
    public async Task<string?> ReadPromptTextAsync(
        string? folderPath,
        int maxChars,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || maxChars <= 0)
        {
            return null;
        }

        var filesRoot = ResolveFilesRoot(folderPath);
        if (filesRoot is null || !Directory.Exists(filesRoot))
        {
            return null;
        }

        var builder = new StringBuilder();

        foreach (var file in ListFiles(filesRoot))
        {
            if (builder.Length >= maxChars)
            {
                builder.Append("\n... (solution truncated)");
                break;
            }

            var fullPath = SubmissionPathRules.ResolveUnderRoot(filesRoot, file.Path);
            if (fullPath is null || !File.Exists(fullPath))
            {
                continue;
            }

            string content;
            try
            {
                content = await ReadTextAsync(fullPath, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var remaining = maxChars - builder.Length;
            if (content.Length > remaining)
            {
                content = content[..Math.Max(0, remaining)];
            }

            builder.Append("### ").Append(file.Path).Append('\n');
            builder.Append("````\n").Append(content.TrimEnd('\n')).Append("\n````\n\n");
        }

        var text = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public void Delete(string? folderPath)
    {
        var folder = ResolveFolder(folderPath);
        if (folder is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove the reference folder {Folder}.", folder);
        }
    }

    private string? ResolveFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        var root = Path.Combine(environment.ContentRootPath, uploads.Value.Root);
        return SubmissionPathRules.ResolveUnderRoot(root, folderPath);
    }

    private string? ResolveFilesRoot(string folderPath)
    {
        var folder = ResolveFolder(folderPath);
        return folder is null ? null : Path.Combine(folder, "files");
    }

    private static List<SubmissionFile> ListFiles(string filesRoot)
    {
        var files = new List<SubmissionFile>();

        foreach (var fullPath in Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(filesRoot, fullPath).Replace('\\', '/');
            var resolved = SubmissionPathRules.ResolveUnderRoot(filesRoot, relative);

            if (resolved is null
                || !string.Equals(resolved, Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            files.Add(new SubmissionFile(relative, new FileInfo(fullPath).Length));
        }

        return files
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolveRequestedPath(string filesRoot, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var normalized = SubmissionPathRules.Normalize(requested);
        if (!normalized.Succeeded)
        {
            return null;
        }

        var fullPath = SubmissionPathRules.ResolveUnderRoot(filesRoot, normalized.Path);
        return fullPath is not null && File.Exists(fullPath) ? normalized.Path : null;
    }

    private static async Task<string> ReadTextAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fullPath);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string CleanFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        clean = clean.Trim().Trim('.');

        if (clean.Length == 0)
        {
            clean = "solution";
        }

        return clean.Length > 100 ? clean[..100] : clean;
    }
}
