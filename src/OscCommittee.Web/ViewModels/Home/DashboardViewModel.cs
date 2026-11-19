namespace OscCommittee.Web.ViewModels.Home;

public sealed class DashboardViewModel
{
    public string MonthLabel { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public int MonthTotal { get; init; }

    public int? Rank { get; init; }

    public int CohortSize { get; init; }

    public bool IsBoard { get; init; }

    public DashboardDsSheetViewModel? ActiveDsSheet { get; init; }

    public DashboardTaskViewModel? ActiveTask { get; init; }

    public IReadOnlyList<DashboardAlertViewModel> Alerts { get; init; } = [];

    public Ds.DsSyncButtonViewModel? Sync { get; init; }
}

public sealed class DashboardDsSheetViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public string DueIso { get; init; } = string.Empty;

    public bool IsOverdue { get; init; }

    public int Solved { get; init; }

    public int Total { get; init; }

    public int Points { get; init; }

    public int ProgressPercent => Total == 0 ? 0 : (int)Math.Round(Solved * 100d / Total);
}

public sealed class DashboardTaskViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public string DueIso { get; init; } = string.Empty;

    public bool IsOverdue { get; init; }

    public string StatusLabel { get; init; } = string.Empty;

    public int? Score { get; init; }

    public int Attempts { get; init; }

    public bool IsLate { get; init; }

    public int MaxPoints { get; init; } = Services.Tasks.ICSharpTaskService.MaxPoints;
}

public sealed class DashboardAlertViewModel
{
    public string Kind { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Href { get; init; }

    public string? ActionLabel { get; init; }

    public string Icon => Kind switch
    {
        "handle" => "fa-solid fa-id-card",
        "failed" => "fa-solid fa-circle-exclamation",
        "improve" => "fa-solid fa-arrow-trend-up",
        _ => "fa-solid fa-circle-info"
    };

    public string AccentClass => Kind switch
    {
        "failed" => "border-danger/30 bg-danger/10 text-danger",
        "improve" => "border-info/30 bg-info/10 text-info",
        _ => "border-warning/30 bg-warning/10 text-warning"
    };
}