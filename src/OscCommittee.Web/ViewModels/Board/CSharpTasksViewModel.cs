using System.ComponentModel.DataAnnotations;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Tasks;
using OscCommittee.Web.ViewModels.Tasks;

namespace OscCommittee.Web.ViewModels.Board;

public sealed class CSharpTaskRowViewModel
{
    public Guid Id { get; init; }

    public Guid MonthSheetId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool IsHidden { get; init; }

    public bool HasReference { get; init; }

    public int ReferenceFileCount { get; init; }

    /// <summary>The month's other active task, if any, so the confirm dialog can name it.</summary>
    public string? ActiveTitleInMonth { get; init; }

    public string StatusLabel => IsHidden ? "Hidden" : IsActive ? "Current" : "Past";

    public string StatusBadgeClass => IsHidden ? "badge-hidden" : IsActive ? "badge-active" : "badge-pending";

    public string ReferenceLabel => HasReference ? $"{ReferenceFileCount} files" : "None";

    public bool CanActivate => !IsHidden && !IsActive;

    public bool CanDeactivate => IsActive;
}

public sealed class CSharpTasksViewModel
{
    public IReadOnlyList<CSharpTaskRowViewModel> Tasks { get; init; } = [];

    public int TotalCount { get; init; }

    public int ActiveCount { get; init; }

    public int HiddenCount { get; init; }

    public string CurrentMonthLabel { get; init; } = string.Empty;
}

/// <summary>Create and edit share this form; <see cref="Id"/> decides which action posts.</summary>
public sealed class CSharpTaskFormViewModel
{
    public Guid? Id { get; init; }

    public bool IsEdit => Id.HasValue;

    [Required(ErrorMessage = "Enter a title.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "Use a title between 3 and 120 characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Statement (Markdown)")]
    public string? DescriptionMd { get; set; }

    [Required(ErrorMessage = "Pick the month this task belongs to.")]
    [Display(Name = "Month")]
    public Guid? MonthSheetId { get; set; }

    /// <summary>Local deadline date in <c>yyyy-MM-dd</c>, as submitted by a date input.</summary>
    [Required(ErrorMessage = "Pick a deadline.")]
    [Display(Name = "Deadline")]
    public string? DueLocal { get; set; }

    [Display(Name = "Solution folder or file")]
    public List<IFormFile>? ReferenceFiles { get; set; }

    [Display(Name = "Starting code (.zip)")]
    public IFormFile? StarterZip { get; set; }

    /// <summary>Set by the controller on GET so an edit keeps the stored starting-code zip.</summary>
    public string? ExistingStarterZipPath { get; set; }

    public string? ExistingStarterZipFileName { get; set; }

    public long ExistingStarterZipSizeBytes { get; set; }

    /// <summary>Board ticked "remove the current starting code".</summary>
    [Display(Name = "Remove the starting code")]
    public bool ClearStarterZip { get; set; }

    /// <summary>Set by the controller on GET so an edit keeps the stored reference.</summary>
    public string? ExistingReferenceFolderPath { get; set; }

    public int ExistingReferenceFileCount { get; set; }

    /// <summary>Board ticked "remove the current reference".</summary>
    [Display(Name = "Remove the current solution")]
    public bool ClearReference { get; set; }

    [Display(Name = "Hidden from members")]
    public bool IsHidden { get; set; }

    [Display(Name = "Current task for this month")]
    public bool IsActive { get; set; }

    public IReadOnlyList<DsMonthOption> Months { get; set; } = [];

    /// <summary>Time zone label for the deadline hint, e.g. <c>Africa/Cairo</c>.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public bool HasExistingReference =>
        !ClearReference && ExistingReferenceFileCount > 0 && !string.IsNullOrWhiteSpace(ExistingReferenceFolderPath);

    public bool HasExistingStarter =>
        !ClearStarterZip && ExistingStarterZipSizeBytes > 0 && !string.IsNullOrWhiteSpace(ExistingStarterZipPath);

    /// <summary>Built by the controller once the local deadline and the reference are resolved.</summary>
    public CSharpTaskRequest? Request { get; set; }
}

public sealed class CSharpTaskDetailViewModel
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public string DueLabel { get; init; } = string.Empty;

    public DateTimeOffset DueAtUtc { get; init; }

    public bool IsActive { get; init; }

    public bool IsHidden { get; init; }

    public bool HasStarterZip { get; init; }

    public string? StarterZipFileName { get; init; }

    public long StarterZipSizeBytes { get; init; }

    public bool HasReference { get; init; }

    public string? ReferenceFolderPath { get; init; }

    public SubmissionFilesView? ReferenceFiles { get; init; }

    public string StatementHtml { get; init; } = string.Empty;

    public bool HasStatement { get; init; }

    public string StatusLabel => IsHidden ? "Hidden" : IsActive ? "Current" : "Past";

    public string StatusBadgeClass => IsHidden ? "badge-hidden" : IsActive ? "badge-active" : "badge-pending";

    public IReadOnlyList<BoardSubmissionRowViewModel> Attempts { get; init; } = [];
}

/// <summary>A board-side read-only row for one member attempt.</summary>
public sealed class BoardSubmissionRowViewModel
{
    public Guid Id { get; init; }

    public string MemberName { get; init; } = string.Empty;

    public int AttemptNumber { get; init; }

    public string SubmittedLabel { get; init; } = string.Empty;

    public int FileCount { get; init; }

    public bool IsLate { get; init; }

    public SubmissionStatus Status { get; init; }

    public int? Score { get; init; }

    public int? Points { get; init; }

    public string StatusLabel => SubmissionStatusView.Label(Status);

    public string StatusBadgeClass => SubmissionStatusView.BadgeClass(Status);

    public string ScoreLabel => Score?.ToString() ?? "—";

    public string PointsLabel => Points?.ToString() ?? "—";
}