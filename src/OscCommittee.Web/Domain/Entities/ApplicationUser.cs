using Microsoft.AspNetCore.Identity;
using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Domain.Entities;

/// <summary>
/// One person: a member waiting for approval, an active member, or a board account.
/// Ids are time-ordered so the member table sorts naturally by signup.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Access is decided by this value, checked on every request.</summary>
    public UserStatus Status { get; set; } = UserStatus.Pending;

    public string? LeetCodeHandle { get; set; }

    public string? CodeforcesHandle { get; set; }

    public DateTimeOffset? LeetCodeVerifiedAtUtc { get; set; }

    public DateTimeOffset? CodeforcesVerifiedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset? DeactivatedAtUtc { get; set; }

    public Guid? DeactivatedByUserId { get; set; }

    public bool MustChangePassword { get; set; }

    public DateTimeOffset? LastSeenAtUtc { get; set; }

    public bool IsActive => Status == UserStatus.Active;
}
