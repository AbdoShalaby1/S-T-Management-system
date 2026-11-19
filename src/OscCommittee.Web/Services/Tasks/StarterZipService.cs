using System.IO.Compression;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Uploads;

namespace OscCommittee.Web.Services.Tasks;

/// <summary>The stored starting-code zip after a successful upload.</summary>
public sealed record StarterZipUpload(
    string FilePath,
    string FileName,
    long SizeBytes);

/// <summary>A stored starting-code zip ready to be served as a download.</summary>
public sealed record StarterZipDownload(
    string FullPath,
    string FileName,
    long SizeBytes);

/// <summary>
/// Stores the board's optional starting-code zip under <c>{Uploads:Root}/starters/{guid}/starter.zip</c>
/// so members can download it, build on it, and upload their own folder. Reads are by the stored
/// relative path only.
/// </summary>
public interface IStarterZipService
{
    Task<Result<StarterZipUpload>> SaveAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default);

    StarterZipDownload? Read(string? filePath, string? fileName);

    /// <summary>Best-effort removal of a replaced or cleared starting-code zip. Never throws.</summary>
    void Delete(string? filePath);
}

public sealed class StarterZipService(
    IWebHostEnvironment environment,
    IOptions<UploadsOptions> uploads,
    ILogger<StarterZipService> logger) : IStarterZipService
{
    public async Task<Result<StarterZipUpload>> SaveAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return Result.Fail<StarterZipUpload>(
                "empty",
                "Choose a .zip file for the starting code.");
        }

        var originalName = Path.GetFileName(file.FileName).Trim();
        if (!string.Equals(Path.GetExtension(originalName), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<StarterZipUpload>(
                "not_zip",
                "Upload the starting code as a .zip file.");
        }

        var maxMb = uploads.Value.MaxTotalMb;
        if (file.Length > uploads.Value.MaxTotalBytes)
        {
            return Result.Fail<StarterZipUpload>(
                "too_large",
                $"Keep the starting-code zip under {maxMb} MB.");
        }

        if (!await IsReadableZipAsync(file))
        {
            return Result.Fail<StarterZipUpload>(
                "invalid_zip",
                "That file is not a readable .zip archive.");
        }

        var relativePath = $"starters/{Guid.CreateVersion7():N}/starter.zip";
        var target = ResolveFile(relativePath);
        if (target is null)
        {
            return Result.Fail<StarterZipUpload>("path_escape", UploadCopy.PathEscape);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using var source = file.OpenReadStream();
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Storing the starting-code zip failed.");
            Delete(relativePath);

            return Result.Fail<StarterZipUpload>(
                "storage_failed",
                "The starting code could not be saved. Try again; if it keeps failing, tell the board.");
        }

        return Result.Ok(
            new StarterZipUpload(relativePath, CleanFileName(originalName), file.Length),
            "Starting code saved.");
    }

    public StarterZipDownload? Read(string? filePath, string? fileName)
    {
        var fullPath = ResolveFile(filePath);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return null;
        }

        var size = new FileInfo(fullPath).Length;
        return new StarterZipDownload(fullPath, CleanFileName(fileName), size);
    }

    public void Delete(string? filePath)
    {
        var fullPath = ResolveFile(filePath);
        if (fullPath is null)
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(fullPath);
            if (folder is not null && Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not remove the starting-code folder for {Path}.", fullPath);
        }
    }

    private string? ResolveFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var root = Path.Combine(environment.ContentRootPath, uploads.Value.Root);
        return SubmissionPathRules.ResolveUnderRoot(root, filePath);
    }

    private static async Task<bool> IsReadableZipAsync(IFormFile file)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            return archive.Entries.Count > 0;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException)
        {
            return false;
        }
    }

    private static string CleanFileName(string? name)
    {
        var candidate = Path.GetFileName(name ?? string.Empty).Trim();
        var invalid = Path.GetInvalidFileNameChars();
        candidate = new string(candidate.Select(character => invalid.Contains(character) ? '-' : character).ToArray())
            .Trim()
            .Trim('.');

        if (candidate.Length == 0)
        {
            return "starting-code.zip";
        }

        if (!candidate.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            candidate += ".zip";
        }

        return candidate.Length > 120 ? candidate[..120] : candidate;
    }
}