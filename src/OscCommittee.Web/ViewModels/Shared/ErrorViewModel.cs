namespace OscCommittee.Web.ViewModels.Shared;

public sealed record ErrorViewModel
{
    public int StatusCode { get; init; } = 500;

    public string Title { get; init; } = "Something went wrong";

    public string Message { get; init; } = string.Empty;

    /// <summary>Correlation id shown to the member so a board member can find the log entry.</summary>
    public string? RequestId { get; init; }

    public bool ShowRequestId => !string.IsNullOrWhiteSpace(RequestId);
}
