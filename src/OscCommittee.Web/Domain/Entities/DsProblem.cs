using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// One problem on a DS sheet (PLAN §11). <see cref="Points"/> is materialised from
/// <see cref="PointsOverride"/> or the board's difficulty presets, so a sync can credit the right
/// amount without re-reading settings.
/// </summary>
public sealed class DsProblem
{
    public DsProblem()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public Guid DsSheetId { get; set; }

    public Platform Platform { get; set; }

    /// <summary>LeetCode slug, or <c>{contestId}:{index}</c> for Codeforces.</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Canonical public URL, safe to show members.</summary>
    public string Url { get; set; } = string.Empty;

    public Difficulty Difficulty { get; set; }

    /// <summary>Null means "use the preset for <see cref="Difficulty"/>".</summary>
    public int? PointsOverride { get; set; }

    /// <summary>Materialised <c>PointsOverride ?? preset[Difficulty]</c>.</summary>
    public int Points { get; set; }

    /// <summary>Codeforces rating, or null for LeetCode.</summary>
    public int? Rating { get; set; }

    /// <summary>LeetCode premium flag; members are warned before clicking through.</summary>
    public bool IsPaidOnly { get; set; }

    /// <summary>Only submissions at or after this instant can credit points.</summary>
    public DateTimeOffset AssignedAtUtc { get; set; }

    public int SortOrder { get; set; }

    public DsSheet? DsSheet { get; set; }
}