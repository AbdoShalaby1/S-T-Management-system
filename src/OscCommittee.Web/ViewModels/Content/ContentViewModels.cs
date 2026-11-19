using System.ComponentModel.DataAnnotations;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Content;

namespace OscCommittee.Web.ViewModels.Content;

/// <summary>One card on the member content list.</summary>
public sealed class ContentCardViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string UpdatedLabel { get; init; } = string.Empty;

    public string Href { get; init; } = string.Empty;
}

public sealed class ContentIndexViewModel
{
    public IReadOnlyList<ContentCardViewModel> Pages { get; init; } = [];
}

/// <summary>The rendered member page plus its table of contents.</summary>
public sealed class ContentPageViewModel
{
    public string Title { get; init; } = string.Empty;

    public string BodyHtml { get; init; } = string.Empty;

    public IReadOnlyList<MarkdownHeading> Headings { get; init; } = [];

    public string UpdatedLabel { get; init; } = string.Empty;
}

/// <summary>One row on <c>/board/content</c>.</summary>
public sealed class BoardContentRowViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public bool IsPublished { get; init; }

    public string UpdatedLabel { get; init; } = string.Empty;

    public string? UpdatedByName { get; init; }

    public string PublicHref { get; init; } = string.Empty;
}

public sealed class BoardContentListViewModel
{
    public IReadOnlyList<BoardContentRowViewModel> Pages { get; init; } = [];

    public int PublishedCount { get; init; }

    public int DraftCount { get; init; }
}

/// <summary>
/// The board editor: title, Markdown body, images and the publish flag. Data annotations cover the
/// simple shape; the service re-checks everything because form posts are untrusted.
/// </summary>
public sealed class ContentFormViewModel
{
    public Guid Id { get; set; }

    public bool IsEdit => Id != Guid.Empty;

    [Required(ErrorMessage = "Give the page a title.")]
    [StringLength(
        ContentRules.MaxTitleLength,
        MinimumLength = ContentRules.MinTitleLength,
        ErrorMessage = "Use a title between 3 and 160 characters.")]
    public string? Title { get; set; }

    [Display(Name = "Content")]
    [StringLength(ContentRules.MaxBodyChars, ErrorMessage = "The body is too large. Keep it under 200,000 characters.")]
    public string? BodyMarkdown { get; set; }

    public bool IsPublished { get; set; }

    public IReadOnlyList<ContentImage> Images { get; set; } = [];

    public string PublicHref { get; set; } = string.Empty;

    public string? UpdatedLabel { get; set; }

    public string? UpdatedByName { get; set; }

    public int MaxBodyChars => ContentRules.MaxBodyChars;

    public string? LastImageUrl { get; set; }

    public ContentPageRequest ToRequest() => new(
        Title ?? string.Empty,
        BodyMarkdown,
        IsPublished);
}