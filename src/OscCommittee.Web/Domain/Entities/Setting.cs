namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// Editable configuration that outlives a deployment: point presets, the grading prompt,
/// upload limits and point presets (PLAN §11). Seeded once, then owned by the board.
/// </summary>
public sealed class Setting
{
    /// <summary>Dotted key, e.g. <c>Points.Easy</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}
