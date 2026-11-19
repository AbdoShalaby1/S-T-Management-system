using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Accounts;

/// <summary>Display-ready facts about one account, shared by every board list.</summary>
public sealed record MemberSummary(
    Guid Id,
    string DisplayName,
    string Email,
    UserStatus Status,
    string? LeetCodeHandle,
    string? CodeforcesHandle,
    bool IsBoard,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? DeactivatedAtUtc,
    DateTimeOffset? LastSeenAtUtc)
{
    public bool HasMissingHandles =>
        string.IsNullOrWhiteSpace(LeetCodeHandle) || string.IsNullOrWhiteSpace(CodeforcesHandle);
}

public sealed record MemberFilter(
    UserStatus? Status = null,
    bool OnlyMissingHandles = false,
    string? Sort = null);

/// <summary>Population counts for the members page, independent of the active filter.</summary>
public sealed record MemberCounts(int Total, int Active, int Pending, int Deactivated);

/// <summary>Approval, membership and deactivation rules from PLAN §5 and §8.</summary>
public interface IMemberAdminService
{
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);

    Task<int> CountActiveMembersAsync(CancellationToken cancellationToken = default);

    Task<int> CountActiveBoardAsync(CancellationToken cancellationToken = default);

    Task<MemberCounts> GetCountsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberSummary>> GetPendingAsync(int take = 50, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberSummary>> GetMembersAsync(MemberFilter filter, CancellationToken cancellationToken = default);

    Task<MemberSummary?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ApproveAsync(Guid userId, Guid boardUserId, CancellationToken cancellationToken = default);

    Task<Result> RejectAsync(Guid userId, Guid boardUserId, string? note, CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(Guid userId, Guid boardUserId, CancellationToken cancellationToken = default);

    Task<Result> RestoreAsync(Guid userId, Guid boardUserId, CancellationToken cancellationToken = default);
}
