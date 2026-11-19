namespace OscCommittee.Web.ViewModels.Board;

public sealed class BoardDashboardViewModel
{
    public string MonthLabel { get; init; } = string.Empty;

    public int PendingCount { get; init; }

    public int ActiveMemberCount { get; init; }

    public IReadOnlyList<ApprovalRowViewModel> RecentPending { get; init; } = [];
}
