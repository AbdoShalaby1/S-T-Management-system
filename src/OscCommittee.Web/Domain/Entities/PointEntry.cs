using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// A credit in a month's ledger (PLAN §6.4). Every total in the app sums
/// <see cref="FinalPoints"/>; <see cref="BasePoints"/> keeps the value it was credited with.
/// </summary>
public sealed class PointEntry
{
    public PointEntry()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid MonthSheetId { get; set; }

    public PointCategory Category { get; set; }

    /// <summary>Solve or submission id; null for a bonus.</summary>
    public Guid? SourceId { get; set; }

    public int BasePoints { get; set; }

    public int FinalPoints { get; set; }

    /// <summary>Set when the board edited the score; preset recalculation leaves these alone.</summary>
    public bool IsManualOverride { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Null for credits the system created.</summary>
    public Guid? CreatedByUserId { get; set; }

    public ApplicationUser? User { get; set; }

    public MonthSheet? MonthSheet { get; set; }
}