namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// One graded C# task inside a month (PLAN §11). Members read the statement, optionally download a
/// starting-code zip to build on, and upload a folder; the board owns an optional reference folder,
/// which members see automatically once the deadline passes.
/// </summary>
public sealed class CSharpTask
{
    public CSharpTask()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public Guid MonthSheetId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The statement, rendered as Markdown with a table of contents.</summary>
    public string? DescriptionMd { get; set; }

    /// <summary>
    /// Relative path under <c>Uploads:Root</c> of the board's starting-code zip, e.g.
    /// <c>starters/{guid}/starter.zip</c>. Null when the task has no starting code.
    /// </summary>
    public string? StarterZipPath { get; set; }

    /// <summary>The uploaded zip's original file name, used for the download.</summary>
    public string? StarterZipFileName { get; set; }

    /// <summary>Size of the starting-code zip in bytes; zero when there is none.</summary>
    public long StarterZipSizeBytes { get; set; }

    /// <summary>
    /// Relative path under <c>Uploads:Root</c> of the board's uploaded reference folder, e.g.
    /// <c>references/{guid}</c>. Null when the task has no reference.
    /// </summary>
    public string? ReferenceFolderPath { get; set; }

    /// <summary>Files kept in the reference folder; zero means there is no reference.</summary>
    public int ReferenceFileCount { get; set; }

    /// <summary>Required, and must be in the future when the task is created. Holds the end of the chosen local day, in UTC.</summary>
    public DateTimeOffset DueAtUtc { get; set; }

    /// <summary>Featured on the member task list. At most one per month.</summary>
    public bool IsActive { get; set; }

    /// <summary>Draft: members do not see it at all. It can never be active while hidden.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Kept for the existing schema; task order comes from creation time, not this value.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public MonthSheet? MonthSheet { get; set; }

    public ApplicationUser? CreatedBy { get; set; }
}