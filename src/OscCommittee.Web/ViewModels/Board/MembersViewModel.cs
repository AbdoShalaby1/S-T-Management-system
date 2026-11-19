namespace OscCommittee.Web.ViewModels.Board;

public sealed class MembersViewModel
{
    public IReadOnlyList<MemberRowViewModel> Members { get; init; } = [];

    /// <summary><c>null</c> means "any status".</summary>
    public string? StatusFilter { get; init; }

    public bool MissingHandlesOnly { get; init; }

    public string Sort { get; init; } = "name";

    public int TotalCount { get; init; }

    public int ActiveCount { get; init; }

    public int PendingCount { get; init; }

    public int DeactivatedCount { get; init; }
}