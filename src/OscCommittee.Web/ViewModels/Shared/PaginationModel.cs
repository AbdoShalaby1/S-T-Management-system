namespace OscCommittee.Web.ViewModels.Shared;

public sealed record PaginationModel
{
    public int Page { get; init; } = 1;

    public int TotalPages { get; init; } = 1;

    /// <summary>Format string for the page link, e.g. <c>?page={0}</c>.</summary>
    public string HrefTemplate { get; init; } = "?page={0}";
}
