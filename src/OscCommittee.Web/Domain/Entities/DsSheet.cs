namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// One problem set inside a month (PLAN §11). The visible/hidden and active/inactive flags are
/// independent: hidden sheets are drafts, and only one sheet per month may be active.
/// </summary>
public sealed class DsSheet
{
    public DsSheet()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public Guid MonthSheetId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown shown above the problem list. Rendered by the shared Markdown service.</summary>
    public string? DescriptionMd { get; set; }

    /// <summary>Required, and must be in the future when the sheet is created. Holds the end of the chosen local day, in UTC.</summary>
    public DateTimeOffset DueAtUtc { get; set; }

    /// <summary>Featured on the dashboard and pinned on <c>/ds</c>. At most one per month.</summary>
    public bool IsActive { get; set; }

    /// <summary>Draft: members do not see it at all. Existing work is untouched when toggled.</summary>
    public bool IsHidden { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public MonthSheet? MonthSheet { get; set; }

    public ApplicationUser? CreatedBy { get; set; }
}