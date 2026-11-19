using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Standings;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.ViewModels.Shared;

namespace OscCommittee.Web.ViewModels.Standings;

/// <summary>One entry in the tab or view switch.</summary>
public sealed record StandingsSwitchLink(string Label, string Href, bool IsActive);

public sealed class StandingsViewModel
{
    public StandingsTab Tab { get; init; }

    public StandingsView View { get; init; }

    public IReadOnlyList<StandingsSwitchLink> Tabs { get; init; } = [];

    public bool IsBoard { get; init; }

    public bool IncludeDeactivated { get; init; }

    public string IncludeDeactivatedHref { get; init; } = string.Empty;

    public string ExcludeDeactivatedHref { get; init; } = string.Empty;

    public string RankingNote { get; init; } = string.Empty;

    public StandingsSummaryViewModel? Summary { get; init; }

    public StandingsDetailViewModel? Detail { get; init; }
}

public sealed class StandingsSummaryViewModel
{
    public StandingsTab Tab { get; init; }

    public Guid? MonthSheetId { get; init; }

    public string MonthLabel { get; init; } = string.Empty;

    public bool HasMonth { get; init; }

    public IReadOnlyList<StandingsRowViewModel> Rows { get; init; } = [];

    public int ParticipantCount { get; init; }

    public PaginationModel? Pagination { get; init; }

    public StandingsContextViewModel? Context { get; init; }
}

public sealed class StandingsRowViewModel
{
    public int Rank { get; init; }

    public Guid UserId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    public bool IsCurrentUser { get; init; }

    public int DsPoints { get; init; }

    public int TaskPoints { get; init; }

    public int BonusPoints { get; init; }

    public int Total { get; init; }

    public int DsSolved { get; init; }

    public int TasksSolved { get; init; }

    public string DetailHref { get; init; } = string.Empty;
}

public sealed class StandingsContextViewModel
{
    public StandingsDsCardViewModel? DsSheet { get; init; }

    public StandingsTaskCardViewModel? Task { get; init; }
}

public sealed class StandingsDsCardViewModel
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

public sealed class StandingsTaskCardViewModel
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

    public int MaxPoints { get; init; } = SubmissionScoring.MaxPoints;
}

public sealed class StandingsDetailViewModel
{
    public IReadOnlyList<StandingsMemberChoice> Members { get; init; } = [];

    public Guid SelectedMemberId { get; init; }

    public string SelectedMemberName { get; init; } = string.Empty;

    public string SelectedMemberInitials { get; init; } = string.Empty;

    public UserStatus SelectedMemberStatus { get; init; }

    public string SelectedMemberStatusLabel { get; init; } = string.Empty;

    public string SelectedMemberStatusBadgeClass { get; init; } = string.Empty;

    public string RegisteredLabel { get; init; } = string.Empty;

    public IReadOnlyList<StandingsMonthChoice> Months { get; init; } = [];

    public Guid? SelectedMonthId { get; init; }

    public string SelectedMonthLabel { get; init; } = string.Empty;

    public bool SelectedMonthIsOpen { get; init; }

    public StandingsMonthTotalsViewModel? SelectedTotals { get; init; }

    public IReadOnlyList<StandingsMonthRowViewModel> MonthRows { get; init; } = [];

    public IReadOnlyList<StandingsSheetRowViewModel> Sheets { get; init; } = [];

    public IReadOnlyList<StandingsTaskRowViewModel> Tasks { get; init; } = [];

    public IReadOnlyList<StandingsBonusRowViewModel> Bonuses { get; init; } = [];

    public bool HasAnyData => MonthRows.Count > 0;

    /// <summary>Board detail lets the board pick a member; a member's own points page does not.</summary>
    public bool CanPickMember { get; init; }

    /// <summary>Where the member/month filter form posts: <c>/standings</c> or <c>/points</c>.</summary>
    public string FilterHref { get; init; } = "/standings";

    /// <summary>Hidden input values the GET filter form posts back.</summary>
    public string TabSlug { get; init; } = "all";

    public bool IncludeDeactivated { get; init; }
}

public sealed record StandingsMemberChoice(
    Guid Id,
    string Label,
    bool IsSelected,
    UserStatus Status);

public sealed record StandingsMonthChoice(
    Guid Id,
    string Value,
    string Label,
    bool IsSelected);

public sealed class StandingsMonthTotalsViewModel
{
    public int DsSolved { get; init; }

    public int DsPoints { get; init; }

    public int TasksSolved { get; init; }

    public int TaskPoints { get; init; }

    public int BonusPoints { get; init; }

    public int Total => DsPoints + TaskPoints + BonusPoints;
}

public sealed class StandingsMonthRowViewModel
{
    public Guid MonthSheetId { get; init; }

    public string Label { get; init; } = string.Empty;

    public MonthStatus Status { get; init; }

    public bool IsSelected { get; init; }

    public string Href { get; init; } = string.Empty;

    public int DsSolved { get; init; }

    public int DsPoints { get; init; }

    public int TasksSolved { get; init; }

    public int TaskPoints { get; init; }

    public int BonusPoints { get; init; }

    public int Total => DsPoints + TaskPoints + BonusPoints;
}

public sealed class StandingsSheetRowViewModel
{
    public Guid SheetId { get; init; }

    public string Title { get; init; } = string.Empty;

    public int Solved { get; init; }

    public int Total { get; init; }

    public int Points { get; init; }
}

public sealed class StandingsTaskRowViewModel
{
    public Guid TaskId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string TaskHref { get; init; } = string.Empty;

    /// <summary>Board-only review link. Members never receive it.</summary>
    public string? AttemptHref { get; init; }

    public int AttemptCount { get; init; }

    public int? Score { get; init; }

    public int Points { get; init; }

    public bool IsLate { get; init; }

    public string StatusLabel { get; init; } = string.Empty;

    public string StatusBadgeClass { get; init; } = "badge-hidden";

    public string ScoreLabel => Score is { } score ? $"{score}/{SubmissionScoring.MaxPoints}" : "—";

    public string AttemptsLabel => AttemptCount switch
    {
        0 => "No attempts",
        1 => "1 attempt",
        _ => $"{AttemptCount} attempts"
    };
}

public sealed class StandingsBonusRowViewModel
{
    public string Label { get; init; } = string.Empty;

    public string BadgeClass { get; init; } = "badge-hidden";

    public string? Reason { get; init; }

    public int Points { get; init; }

    public string AddedLabel { get; init; } = string.Empty;

    public bool IsManual { get; init; }
}