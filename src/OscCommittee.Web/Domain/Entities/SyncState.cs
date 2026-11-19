using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// Per-member, per-platform sync bookkeeping. There is no cursor: every sync re-reads the
/// platform's recent window and the solve table does the dedupe (PLAN §10.1).
/// </summary>
public sealed class SyncState
{
    public Guid UserId { get; set; }

    public SyncPlatform Platform { get; set; }

    public DateTimeOffset? LastSyncAtUtc { get; set; }

    /// <summary>Diagnostic only: the platform reported a full window last time.</summary>
    public bool HistoryGapPossible { get; set; }

    public int FailureCount { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ApplicationUser? User { get; set; }
}
