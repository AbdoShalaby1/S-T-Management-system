namespace OscCommittee.Web.ViewModels.Board;

/// <summary>One person waiting for approval.</summary>
public sealed class ApprovalRowViewModel
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string RequestedOnLabel { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    public string? LeetCodeHandle { get; init; }

    public string? CodeforcesHandle { get; init; }

    public bool HasHandles =>
        !string.IsNullOrWhiteSpace(LeetCodeHandle) || !string.IsNullOrWhiteSpace(CodeforcesHandle);
}
