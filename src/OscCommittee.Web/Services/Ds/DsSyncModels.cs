using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Services.Ds;

/// <summary>One platform's sync state, as the UI needs it.</summary>
public sealed record DsPlatformStatus(
    SyncPlatform Platform,
    bool Linked,
    DateTimeOffset? LastSyncAtUtc,
    bool HistoryGapPossible,
    bool InProgress,
    int FailureCount,
    string? LastError);

public sealed record DsSyncStatus(IReadOnlyList<DsPlatformStatus> Platforms)
{
    public bool HasAnyHandle => Platforms.Any(platform => platform.Linked);

    public bool InProgress => Platforms.Any(platform => platform.InProgress);

    /// <summary>True when the history window may have hidden older accepted submissions.</summary>
    public bool HistoryGapPossible => Platforms.Any(platform => platform.Linked && platform.HistoryGapPossible);

    public bool CanSync => HasAnyHandle && !InProgress;
}

/// <summary>What happened on one platform during a sync.</summary>
public sealed record DsPlatformSyncResult(
    SyncPlatform Platform,
    bool Succeeded,
    bool Skipped,
    string? ErrorCode,
    string Message,
    int NewSolves,
    int Points,
    bool HistoryGapPossible);

/// <summary>A per-problem line the member sees in the sync toast.</summary>
public sealed record DsSyncProblemResult(
    string Title,
    string PlatformLabel,
    int Points,
    bool IsLate);

public sealed record DsSyncResult(
    bool Succeeded,
    string? ErrorCode,
    string Message,
    IReadOnlyList<DsPlatformSyncResult> Platforms,
    IReadOnlyList<DsSyncProblemResult> Problems)
{
    public int NewSolves => Platforms.Sum(platform => platform.NewSolves);

    public int Points => Platforms.Sum(platform => platform.Points);

    public bool HistoryGapPossible => Platforms.Any(platform => platform.HistoryGapPossible);

    public static DsSyncResult Fail(string errorCode, string message) =>
        new(false, errorCode, message, [], []);
}

public interface IDsSyncService
{
    /// <summary>Handles, progress and the history-gap flag for the sync button.</summary>
    Task<DsSyncStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs both platforms for a member. Never throws for a platform failure: the summary carries
    /// <c>rate_limited</c>, <c>not_found</c> or <c>*_unavailable</c> instead.
    /// </summary>
    Task<DsSyncResult> SyncAsync(Guid userId, CancellationToken cancellationToken = default);
}