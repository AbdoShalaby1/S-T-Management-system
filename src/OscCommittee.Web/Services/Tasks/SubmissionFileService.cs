using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Data;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Web.Services.Tasks;

public sealed record SubmissionFile(string Path, long Bytes);

/// <summary>The stored file tree plus the one file the review page is showing.</summary>
public sealed record SubmissionFilesView(
    IReadOnlyList<SubmissionFile> Files,
    string? SelectedPath,
    string? Content,
    string Language,
    bool Truncated,
    string? Error);

public sealed record SubmissionArchive(byte[] Bytes, string FileName);

/// <summary>
/// Reads a submission's stored files by id only. The member id is never part of the path and every
/// resolved path is confined to <c>{root}/{userId}/{submissionId}/files</c> (PLAN §8.7, §15).
/// </summary>
public interface ISubmissionFileService
{
    Task<SubmissionFilesView?> ReadAsync(
        Guid submissionId,
        string? selectedPath,
        CancellationToken cancellationToken = default);

    Task<Result<SubmissionArchive>> BuildArchiveAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default);
}

public sealed class SubmissionFileService(
    AppDbContext db,
    IWebHostEnvironment environment,
    IOptions<UploadsOptions> uploads,
    ILogger<SubmissionFileService> logger) : ISubmissionFileService
{
    private const int MaxDisplayChars = 120_000;

    public async Task<SubmissionFilesView?> ReadAsync(
        Guid submissionId,
        string? selectedPath,
        CancellationToken cancellationToken = default)
    {
        var submission = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(row => row.Id == submissionId)
            .Select(row => new { row.StoredFolderPath })
            .FirstOrDefaultAsync(cancellationToken);

        if (submission is null)
        {
            return null;
        }

        var filesRoot = ResolveFilesRoot(submission.StoredFolderPath);
        if (filesRoot is null || !Directory.Exists(filesRoot))
        {
            return new SubmissionFilesView([], null, null, "csharp", false,
                "The stored files are missing. The board can retry grading or ask the member to upload again.");
        }

        var files = ListFiles(filesRoot);
        if (files.Count == 0)
        {
            return new SubmissionFilesView([], null, null, "csharp", false,
                "This attempt has no stored files.");
        }

        var path = ResolveRequestedPath(filesRoot, selectedPath)
                   ?? files.FirstOrDefault(file => file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))?.Path
                   ?? files[0].Path;

        var fullPath = SubmissionPathRules.ResolveUnderRoot(filesRoot, path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return new SubmissionFilesView(files, null, null, "csharp", false,
                "That file is not part of this submission.");
        }

        string content;
        try
        {
            content = await ReadTextAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Reading stored file {Path} failed.", fullPath);
            return new SubmissionFilesView(files, path, null, LanguageFor(path), false,
                "That file could not be read from disk.");
        }

        var truncated = content.Length > MaxDisplayChars;
        if (truncated)
        {
            content = content[..MaxDisplayChars] +
                      "\n\n// Display truncated. Download the folder to read the whole file.";
        }

        return new SubmissionFilesView(files, path, content, LanguageFor(path), truncated, null);
    }

    public async Task<Result<SubmissionArchive>> BuildArchiveAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.CSharpSubmissions
            .AsNoTracking()
            .Where(submission => submission.Id == submissionId)
            .Select(submission => new
            {
                submission.StoredFolderPath,
                submission.AttemptNo,
                TaskTitle = submission.Task!.Title,
                MemberName = submission.User!.DisplayName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return Result.Fail<SubmissionArchive>("not_found", "That attempt no longer exists.");
        }

        var filesRoot = ResolveFilesRoot(row.StoredFolderPath);
        if (filesRoot is null || !Directory.Exists(filesRoot))
        {
            return Result.Fail<SubmissionArchive>(
                "missing_files",
                "The stored files are missing, so there is nothing to download.");
        }

        var files = ListFiles(filesRoot);
        if (files.Count == 0)
        {
            return Result.Fail<SubmissionArchive>("empty", "This attempt has no stored files.");
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

        var name = BuildFileName(row.MemberName, row.TaskTitle, row.AttemptNo);
        return Result.Ok(new SubmissionArchive(buffer.ToArray(), name));
    }

    /// <summary>Only <c>files/</c> is exposed; <c>bundle.md</c> is an internal grading artefact.</summary>
    private string? ResolveFilesRoot(string storedFolderPath)
    {
        var root = Path.Combine(environment.ContentRootPath, uploads.Value.Root);
        var folder = SubmissionPathRules.ResolveUnderRoot(root, storedFolderPath);
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

    private static string BuildFileName(string memberName, string taskTitle, int attemptNo)
    {
        var raw = $"{memberName}-{taskTitle}-attempt-{attemptNo}";
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(raw.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        clean = clean.Trim().Trim('.');

        if (clean.Length == 0)
        {
            clean = "submission";
        }

        return (clean.Length > 100 ? clean[..100] : clean) + ".zip";
    }

    /// <summary>Prism is loaded with C#, JSON, Markdown, Bash and the core markup language.</summary>
    public static string LanguageFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".json" => "json",
            ".md" or ".markdown" => "markdown",
            ".sh" or ".bash" => "bash",
            ".csproj" or ".sln" or ".xml" or ".yml" or ".yaml" => "markup",
            _ => "markup"
        };
}