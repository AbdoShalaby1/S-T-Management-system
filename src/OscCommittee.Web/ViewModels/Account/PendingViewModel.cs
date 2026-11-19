using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.ViewModels.Account;

public sealed class PendingViewModel
{
    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public UserStatus Status { get; init; }

    /// <summary>Display-zone date the account was requested, e.g. <c>18 November 2026</c>.</summary>
    public string RequestedOnLabel { get; init; } = string.Empty;
}
