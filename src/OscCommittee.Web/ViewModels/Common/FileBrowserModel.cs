using OscCommittee.Web.Services.Tasks;

namespace OscCommittee.Web.ViewModels.Common;

/// <summary>
/// Input for the shared file browser used by the reference and stored-submission panels.
/// <see cref="FileUrl"/> turns a stored relative path into the URL that re-renders this page
/// with that file selected.
/// </summary>
public sealed record FileBrowserModel(
    SubmissionFilesView View,
    string Heading,
    string? Hint = null,
    string? DownloadUrl = null,
    string DownloadLabel = "Download .zip",
    string EmptyTitle = "No files on disk",
    string EmptyDescription = "There is nothing to show here yet.")
{
    public Func<string, string>? FileUrl { get; init; }

    /// <summary>Renders just the list and preview, for callers that supply their own card shell.</summary>
    public bool Bare { get; init; }

    public string? SelectedPath => View.SelectedPath;

    public string? SelectedContent => View.Content;

    public string SelectedLanguage => View.Language;

    public bool SelectedTruncated => View.Truncated;

    public string? FileError => View.Error;

    public static string SizeLabel(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };
}