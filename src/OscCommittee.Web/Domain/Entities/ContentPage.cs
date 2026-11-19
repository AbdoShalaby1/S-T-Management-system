namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// A board-authored Markdown page (PLAN §7.5 and §8.9). Pages are addressed by their id, so the
/// board only supplies a title and body; unpublished pages are drafts and never resolve for members.
/// </summary>
public sealed class ContentPage
{
    public ContentPage()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The page body. Rendered through the shared Markdown pipeline.</summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>Drafts stay invisible to members until the board publishes them.</summary>
    public bool IsPublished { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public ApplicationUser? CreatedBy { get; set; }

    public ApplicationUser? UpdatedBy { get; set; }
}