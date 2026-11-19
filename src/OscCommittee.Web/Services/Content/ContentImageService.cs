using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Content;

/// <summary>An image already sitting in <c>wwwroot/content/{pageId}/</c>.</summary>
public sealed record ContentImage(string FileName, string Url, long Bytes);

/// <summary>
/// Board-only image storage for content pages (PLAN §10.5). Files live under
/// <c>wwwroot/content/{pageId}/</c> so Markdown can reference them with a normal URL; the
/// allowlist and signature check keep anything else out.
/// </summary>
public interface IContentImageService
{
    /// <summary>Images in one page's folder, name-ordered. Unknown pages return an empty list.</summary>
    IReadOnlyList<ContentImage> List(Guid pageId);

    Task<Result<ContentImage>> SaveAsync(
        Guid pageId,
        IFormFile? file,
        CancellationToken cancellationToken = default);
}

public sealed class ContentImageService(
    IWebHostEnvironment environment) : IContentImageService
{
    public const long MaxBytes = 4 * 1024 * 1024;

    public const string NoFileMessage = "Choose an image to upload.";

    public const string TooLargeMessage = "That image is over 4 MB. Shrink it and try again.";

    public const string WrongTypeMessage = "Upload a PNG, JPEG, GIF or WebP image.";

    public const string InvalidPageMessage = "Save the topic before uploading images.";

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    public IReadOnlyList<ContentImage> List(Guid pageId)
    {
        if (pageId == Guid.Empty)
        {
            return [];
        }

        var directory = PageDirectory(pageId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var key = FolderKey(pageId);
        return Directory
            .EnumerateFiles(directory)
            .Where(path => AllowedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var info = new FileInfo(path);
                return new ContentImage(name, $"/content/{key}/{name}", info.Length);
            })
            .ToList();
    }

    public async Task<Result<ContentImage>> SaveAsync(
        Guid pageId,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (pageId == Guid.Empty)
        {
            return Result.Fail<ContentImage>("invalid_page", InvalidPageMessage);
        }

        if (file is null || file.Length == 0)
        {
            return Result.Fail<ContentImage>("no_file", NoFileMessage);
        }

        if (file.Length > MaxBytes)
        {
            return Result.Fail<ContentImage>("too_large", TooLargeMessage);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return Result.Fail<ContentImage>("wrong_type", WrongTypeMessage);
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            bytes = memory.ToArray();
        }

        if (bytes.Length > MaxBytes)
        {
            return Result.Fail<ContentImage>("too_large", TooLargeMessage);
        }

        if (!ContentRules.LooksLikeImage(extension, bytes))
        {
            return Result.Fail<ContentImage>("wrong_type", WrongTypeMessage);
        }

        var directory = PageDirectory(pageId);
        Directory.CreateDirectory(directory);

        var stem = ContentRules.SafeFileStem(file.FileName);
        var name = stem + extension;
        var counter = 2;

        while (File.Exists(Path.Combine(directory, name)))
        {
            if (counter > 200)
            {
                return Result.Fail<ContentImage>(
                    "too_many",
                    "That name is already used many times. Rename the file and try again.");
            }

            name = $"{stem}-{counter}{extension}";
            counter++;
        }

        var fullPath = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

        return Result.Ok(
            new ContentImage(name, $"/content/{FolderKey(pageId)}/{name}", bytes.Length),
            $"Uploaded {name}.");
    }

    private static string FolderKey(Guid pageId) => pageId.ToString("N");

    private string PageDirectory(Guid pageId)
    {
        var webRoot = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var contentRoot = Path.GetFullPath(Path.Combine(webRoot, "content"));
        var directory = Path.GetFullPath(Path.Combine(contentRoot, FolderKey(pageId)));

        // The key comes from a Guid, so it cannot contain separators; this is belt and braces.
        if (!directory.StartsWith(
            contentRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Content image path escaped the content root.");
        }

        return directory;
    }
}