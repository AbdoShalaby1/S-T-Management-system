namespace OscCommittee.Web.ViewModels.Ds;

using OscCommittee.Web.Services.Ds;

/// <summary>State of the sync button. Built from <see cref="DsSyncStatus"/>.</summary>
public sealed class DsSyncButtonViewModel
{
    public bool HasAnyHandle { get; init; }

    public bool CanSync { get; init; }

    public bool InProgress { get; init; }

    /// <summary>Where a no-JS form post lands afterwards. Always a local URL.</summary>
    public string ReturnUrl { get; init; } = "/ds";

    public static DsSyncButtonViewModel From(DsSyncStatus status, string returnUrl) => new()
    {
        HasAnyHandle = status.HasAnyHandle,
        CanSync = status.CanSync,
        InProgress = status.InProgress,
        ReturnUrl = returnUrl
    };
}

public sealed class DsSheetCardViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    /// <summary>Round-trip UTC timestamp the countdown island reads.</summary>
    public string DueIso { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool IsOverdue { get; init; }

    public int ProblemCount { get; init; }
}

public sealed class DsMonthGroupViewModel
{
    public int Year { get; init; }

    public int Month { get; init; }

    public string Label { get; init; } = string.Empty;

    public bool IsCurrent { get; init; }

    public IReadOnlyList<DsSheetCardViewModel> Sheets { get; init; } = [];
}

public sealed class DsOverviewViewModel
{
    public string MonthLabel { get; init; } = string.Empty;

    public DsSheetCardViewModel? ActiveSheet { get; init; }

    public IReadOnlyList<DsMonthGroupViewModel> Groups { get; init; } = [];

    public int SheetCount => Groups.Sum(group => group.Sheets.Count);
}

public sealed class DsSheetPageViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public string DueIso { get; init; } = string.Empty;

    public string ShortDueLabel { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool IsOverdue { get; init; }

    public string DescriptionHtml { get; init; } = string.Empty;

    public bool HasDescription { get; init; }

    public int ProblemCount { get; init; }

    public DsSyncButtonViewModel? Sync { get; init; }

    public IReadOnlyList<DsProblemCardViewModel> Problems { get; init; } = [];
}

/// <summary>One problem as a member sees it, including their own matched submission.</summary>
public sealed class DsProblemCardViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string PlatformLabel { get; init; } = string.Empty;

    public string PlatformBadgeClass { get; init; } = string.Empty;

    public string DifficultyLabel { get; init; } = string.Empty;

    public string DifficultyBadgeClass { get; init; } = string.Empty;

    public int Points { get; init; }

    public int? Rating { get; init; }

    public bool IsPaidOnly { get; init; }

    public bool IsSolved { get; init; }

    public bool IsLate { get; init; }

    public int AwardedPoints { get; init; }

    /// <summary>Matched submission time, already formatted in the app time zone.</summary>
    public string SolvedLabel { get; init; } = string.Empty;

    public string? Language { get; init; }

    /// <summary>Submission page, or the problem page for a manual credit.</summary>
    public string? SubmissionUrl { get; init; }

    public bool IsManual { get; init; }

    public string ExternalId { get; init; } = string.Empty;
}