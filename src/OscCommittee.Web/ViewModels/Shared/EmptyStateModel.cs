namespace OscCommittee.Web.ViewModels.Shared;

public sealed record EmptyStateModel
{
    public string Icon { get; init; } = "fa-solid fa-inbox";

    public string Title { get; init; } = "Nothing here yet";

    public string Description { get; init; } = string.Empty;

    public string? ActionLabel { get; init; }

    public string? ActionHref { get; init; }
}
