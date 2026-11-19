namespace OscCommittee.Web.ViewModels.Board;

public sealed class ApprovalsViewModel
{
    public IReadOnlyList<ApprovalRowViewModel> Items { get; init; } = [];
}
