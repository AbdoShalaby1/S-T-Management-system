using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.ViewModels.Board;

/// <summary>One row of the board member table.</summary>
public sealed class MemberRowViewModel
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public UserStatus Status { get; init; }

    public string StatusLabel => UserStatusView.Label(Status);

    public string StatusBadgeClass => UserStatusView.BadgeClass(Status);

    public string Initials { get; init; } = string.Empty;

    public string? LeetCodeHandle { get; init; }

    public string? CodeforcesHandle { get; init; }

    public bool IsBoard { get; init; }

    public string JoinedLabel { get; init; } = string.Empty;

    /// <summary>This month's total, or null when the member has none yet.</summary>
    public int? Points { get; init; }

    public string PointsLabel => Points?.ToString() ?? "—";

    public bool CanDeactivate { get; init; }

    public bool CanRestore { get; init; }

    public bool IsSelf { get; init; }
}
