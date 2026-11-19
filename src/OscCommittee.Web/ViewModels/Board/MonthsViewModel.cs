using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.ViewModels.Board;

public sealed class MonthRowViewModel
{
    public Guid Id { get; init; }

    public string Label { get; init; } = string.Empty;

    public MonthStatus Status { get; init; }

    public bool IsCurrent { get; init; }

    public string OpenedLabel { get; init; } = string.Empty;

    public string ClosedLabel { get; init; } = "—";

    public string ClosedByLabel { get; init; } = "—";

    public bool CanClose => Status == MonthStatus.Open;

    public bool CanOpen => Status == MonthStatus.Closed;
}

public sealed class MonthsViewModel
{
    public IReadOnlyList<MonthRowViewModel> Months { get; init; } = [];

    public int OpenCount { get; init; }

    public int ClosedCount { get; init; }

    public string CurrentMonthLabel { get; init; } = string.Empty;
}
