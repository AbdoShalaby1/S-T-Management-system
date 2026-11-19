namespace OscCommittee.Web.Services.Common;

/// <summary>One heading the table of contents links to. <see cref="Id"/> matches the rendered anchor.</summary>
public sealed record MarkdownHeading(int Level, string Text, string Id);

/// <summary>Rendered Markdown plus the headings extracted from the same parse.</summary>
public sealed record MarkdownContent(string Html, IReadOnlyList<MarkdownHeading> Headings)
{
    public static readonly MarkdownContent Empty = new(string.Empty, []);
}

/// <summary>
/// One Markdown pipeline for the whole app: advanced extensions, auto identifiers for the
/// table of contents, and raw HTML disabled (PLAN §10.5).
/// </summary>
public interface IMarkdownService
{
    string ToHtml(string? markdown);

    /// <summary>Renders the document and returns its headings for the table of contents.</summary>
    MarkdownContent ToContent(string? markdown);
}