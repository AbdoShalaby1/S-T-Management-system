using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Accounts;

/// <summary>Read-only account facts plus the editable profile fields (PLAN §7.6).</summary>
public sealed record ProfileSnapshot(
    Guid Id,
    string DisplayName,
    string Email,
    UserStatus Status,
    string? LeetCodeHandle,
    string? CodeforcesHandle,
    DateTimeOffset? LeetCodeVerifiedAtUtc,
    DateTimeOffset? CodeforcesVerifiedAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool IsBoard);

public sealed record UpdateProfileRequest(string DisplayName, string? LeetCodeHandle, string? CodeforcesHandle);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public interface IProfileService
{
    Task<ProfileSnapshot?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a platform confirmed the handle. Advisory only: a mismatch with the saved
    /// handle is reported but never blocks saving a profile.
    /// </summary>
    Task<Result> MarkHandleVerifiedAsync(
        Guid userId,
        SyncPlatform platform,
        string handle,
        CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
