using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// One calendar month of committee work (PLAN §6.1). Everything a member earns is attributed
/// to exactly one sheet. Closing is not locking: old items stay open until their own deadline.
/// </summary>
public sealed class MonthSheet
{
    public MonthSheet()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; set; }

    public int Year { get; set; }

    /// <summary>1–12.</summary>
    public int Month { get; set; }

    public MonthStatus Status { get; set; } = MonthStatus.Open;

    public DateTimeOffset OpenedAtUtc { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    /// <summary>Null when the rollover job closed it automatically.</summary>
    public Guid? ClosedByUserId { get; set; }

    /// <summary>Guards two board members closing the same sheet at once.</summary>
    public byte[] RowVersion { get; set; } = [];
}
