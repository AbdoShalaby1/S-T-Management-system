using System.ComponentModel.DataAnnotations;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Settings;

namespace OscCommittee.Web.ViewModels.Board;

public sealed class DsSheetRowViewModel
{
    public Guid Id { get; init; }

    public Guid MonthSheetId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool IsHidden { get; init; }

    public int SortOrder { get; init; }

    public int ProblemCount { get; init; }

    /// <summary>The month's other active sheet, if any, so the confirm dialog can name it.</summary>
    public string? ActiveTitleInMonth { get; init; }

    public string StatusLabel => IsHidden ? "Hidden" : IsActive ? "Current" : "Past";

    public string StatusBadgeClass => IsHidden ? "badge-hidden" : IsActive ? "badge-active" : "badge-pending";

    public bool CanActivate => !IsHidden && !IsActive;

    public bool CanDeactivate => IsActive;

    public bool CanHide => !IsHidden;
}

public sealed class DsSheetsViewModel
{
    public IReadOnlyList<DsSheetRowViewModel> Sheets { get; init; } = [];

    public int TotalCount { get; init; }

    public int ActiveCount { get; init; }

    public int HiddenCount { get; init; }

    public string CurrentMonthLabel { get; init; } = string.Empty;
}

public sealed record DsMonthOption(Guid Id, string Label, bool IsCurrent);

/// <summary>Create and edit share this form; <see cref="Id"/> decides which action posts.</summary>
public sealed class DsSheetFormViewModel
{
    public Guid? Id { get; init; }

    public bool IsEdit => Id.HasValue;

    [Required(ErrorMessage = "Enter a title.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "Use a title between 3 and 120 characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Description (Markdown)")]
    public string? DescriptionMd { get; set; }

    [Required(ErrorMessage = "Pick the month this sheet belongs to.")]
    [Display(Name = "Month")]
    public Guid? MonthSheetId { get; set; }

    /// <summary>Local deadline date in <c>yyyy-MM-dd</c>, as submitted by a date input.</summary>
    [Required(ErrorMessage = "Pick a deadline.")]
    [Display(Name = "Deadline")]
    public string? DueLocal { get; set; }

    [Range(0, 999, ErrorMessage = "Sort order must be between 0 and 999.")]
    [Display(Name = "Sort order")]
    public int SortOrder { get; set; }

    [Display(Name = "Hidden from members")]
    public bool IsHidden { get; set; }

    [Display(Name = "Current sheet for this month")]
    public bool IsActive { get; set; }

    public IReadOnlyList<DsMonthOption> Months { get; set; } = [];

    /// <summary>Problems already on this sheet; only populated when editing.</summary>
    public IReadOnlyList<DsProblemRowViewModel> Problems { get; set; } = [];

    /// <summary>Links pasted into the inline add box, kept when one of them is rejected.</summary>
    public string? NewProblemLinks { get; set; }

    /// <summary>Difficulty applied to every problem in the next inline add.</summary>
    public Difficulty NewProblemDifficulty { get; set; } = Difficulty.Medium;

    /// <summary>Per-link rejections from the last inline add attempt.</summary>
    public IReadOnlyList<string> AddProblemErrors { get; set; } = [];

    public PointsPresets Presets { get; set; } = PointsPresets.Default;

    /// <summary>Time zone label for the deadline hint, e.g. <c>Africa/Cairo</c>.</summary>
    public string TimeZoneId { get; set; } = "UTC";
}