using System.ComponentModel.DataAnnotations;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Points;

namespace OscCommittee.Web.ViewModels.Board;

/// <summary>Human wording and badge styling for a ledger category.</summary>
public static class PointCategoryView
{
    public static string Label(PointCategory category) => category switch
    {
        PointCategory.DsSolve => "DS solve",
        PointCategory.CSharpSubmission => "C# task",
        PointCategory.Bonus => "Bonus",
        PointCategory.Adjustment => "Adjustment",
        _ => category.ToString()
    };

    public static string BadgeClass(PointCategory category) => category switch
    {
        PointCategory.DsSolve => "badge-active",
        PointCategory.CSharpSubmission => "badge-pending",
        PointCategory.Bonus => "badge-graded",
        PointCategory.Adjustment => "badge-hidden",
        _ => "badge-hidden"
    };
}

public sealed record PointsMonthOption(Guid Id, string Label, bool IsCurrent, MonthStatus Status);

/// <summary>One row on the board points list.</summary>
public sealed record PointsIndexRow(
    Guid Id,
    string DisplayName,
    string Email,
    string Initials,
    UserStatus Status,
    int MonthPoints,
    int AllTimePoints)
{
    public string StatusLabel => UserStatusView.Label(Status);

    public string StatusBadgeClass => UserStatusView.BadgeClass(Status);
}

/// <summary>All members and their points, the board's starting point for the ledger.</summary>
public sealed class PointsIndexViewModel
{
    public string MonthLabel { get; init; } = string.Empty;

    public IReadOnlyList<PointsIndexRow> Members { get; init; } = [];

    public int MemberCount { get; init; }

    public int MonthPoints { get; init; }

    public int AllTimePoints { get; init; }
}

/// <summary>Bonuses are the only manual entry now, so the form carries no category.</summary>
public sealed class PointsBonusForm
{
    [Range(-999, 999, ErrorMessage = "Enter a whole number between -999 and 999.")]
    public int Amount { get; set; }

    [StringLength(400, ErrorMessage = "Keep the reason under 400 characters.")]
    public string? Reason { get; set; }

    public Guid? MonthSheetId { get; set; }
}

/// <summary>One member's points, split into problems, tasks and bonuses.</summary>
public sealed class MemberPointsViewModel
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    public UserStatus Status { get; init; }

    public bool IsBoard { get; init; }

    public string StatusLabel => UserStatusView.Label(Status);

    public string StatusBadgeClass => UserStatusView.BadgeClass(Status);

    public string CurrentMonthLabel { get; init; } = string.Empty;

    public int CurrentMonthPoints { get; init; }

    public int AllTimePoints { get; init; }

    public int ProblemCount { get; init; }

    public int TaskCount { get; init; }

    public IReadOnlyList<MemberMonthPoints> Months { get; init; } = [];

    public IReadOnlyList<PointsMonthOption> MonthOptions { get; init; } = [];

    public PointsBonusForm Bonus { get; init; } = new();
}

/// <summary>Month-by-month ledger for one member (PLAN §8.8).</summary>
public sealed class MemberDetailViewModel
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Initials { get; init; } = string.Empty;

    public UserStatus Status { get; init; }

    public string StatusLabel => UserStatusView.Label(Status);

    public string StatusBadgeClass => UserStatusView.BadgeClass(Status);

    public bool IsBoard { get; init; }

    public string? LeetCodeHandle { get; init; }

    public string? CodeforcesHandle { get; init; }

    public bool HasMissingHandles =>
        string.IsNullOrWhiteSpace(LeetCodeHandle) || string.IsNullOrWhiteSpace(CodeforcesHandle);

    public string JoinedLabel { get; init; } = string.Empty;

    public string? ApprovedLabel { get; init; }

    public string? DeactivatedLabel { get; init; }

    public Guid? CurrentMonthSheetId { get; init; }

    public string CurrentMonthLabel { get; init; } = string.Empty;

    public int CurrentMonthPoints { get; init; }

    public int AllTimePoints { get; init; }

    public IReadOnlyList<MemberMonthPoints> Months { get; init; } = [];
}