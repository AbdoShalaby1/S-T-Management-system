using System.ComponentModel.DataAnnotations;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Settings;

namespace OscCommittee.Web.ViewModels.Board;

public sealed class DsProblemRowViewModel
{
    public Guid Id { get; init; }

    public Guid DsSheetId { get; init; }

    public string SheetTitle { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public Platform Platform { get; init; }

    public string ExternalId { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public Difficulty Difficulty { get; init; }

    public int? PointsOverride { get; init; }

    public int Points { get; init; }

    public int? Rating { get; init; }

    public bool IsPaidOnly { get; init; }

    public string AssignedLabel { get; init; } = "—";

    public int SortOrder { get; init; }

    public int SolveCount { get; init; }

    public bool IsFirst { get; init; }

    public bool IsLast { get; init; }

    public string PlatformLabel => Platform == Platform.LeetCode ? "LeetCode" : "Codeforces";

    public string PlatformBadgeClass => Platform == Platform.LeetCode ? "badge-platform-lc" : "badge-platform-cf";

    public string DifficultyLabel => Difficulty.ToString();

    public string DifficultyBadgeClass => Difficulty switch
    {
        Difficulty.Easy => "badge-difficulty-easy",
        Difficulty.Medium => "badge-difficulty-medium",
        _ => "badge-difficulty-hard"
    };

    public string PointsLabel => PointsOverride is null ? Points.ToString() : $"{Points} (override)";

    public string RatingLabel => Rating is null ? "—" : Rating.Value.ToString();
}

/// <summary>The problems panel rendered under the sheet editor.</summary>
public sealed class DsProblemsPanelViewModel
{
    public Guid SheetId { get; init; }

    public string SheetTitle { get; init; } = string.Empty;

    public IReadOnlyList<DsProblemRowViewModel> Problems { get; init; } = [];

    /// <summary>Links pasted into the inline add box, kept when one of them is rejected.</summary>
    public string? NewLinks { get; init; }

    /// <summary>Difficulty applied to every problem in the next inline add.</summary>
    public Difficulty NewDifficulty { get; init; } = Difficulty.Medium;

    /// <summary>Per-link rejections from the last inline add attempt.</summary>
    public IReadOnlyList<string> AddErrors { get; init; } = [];

    /// <summary>True while the sheet page is in view mode, so mutating controls start hidden.</summary>
    public bool ReadOnly { get; init; }

    public PointsPresets Presets { get; init; } = PointsPresets.Default;
}

public sealed record DsSheetOption(Guid Id, string Label);

/// <summary>The add-problem form: metadata is imported from the URL, then the board saves it.</summary>
public sealed class DsProblemFormViewModel
{
    [Required(ErrorMessage = "Paste the problem URL.")]
    [Display(Name = "Problem URL")]
    public string? Url { get; set; }

    [Display(Name = "Sheet")]
    public Guid? DsSheetId { get; set; }

    [Display(Name = "Platform")]
    public Platform? Platform { get; set; }

    [Display(Name = "Platform id")]
    public string? ExternalId { get; set; }

    [Required(ErrorMessage = "Enter the problem title.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Use a title between 3 and 200 characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Difficulty")]
    public Difficulty Difficulty { get; set; } = Difficulty.Medium;

    [Range(0, 999, ErrorMessage = "Points override must be between 0 and 999.")]
    [Display(Name = "Points override")]
    public int? PointsOverride { get; set; }

    public int? Rating { get; set; }

    public bool IsPaidOnly { get; set; }

    /// <summary>Reference line from the platform, e.g. <c>Codeforces rating 1500</c>.</summary>
    public string? Reference { get; set; }

    /// <summary>Import warning (platform unreachable, metadata missing, …).</summary>
    public string? Warning { get; set; }

    /// <summary>True once metadata has been fetched, so the form can switch to "save".</summary>
    public bool HasMetadata { get; set; }

    public IReadOnlyList<DsSheetOption> Sheets { get; set; } = [];

    public PointsPresets Presets { get; set; } = PointsPresets.Default;

    /// <summary>Where the form came from, so Cancel can return to the right place.</summary>
    public string? ReturnUrl { get; set; }
}

public sealed class DsProblemSolversViewModel
{
    public Guid ProblemId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string SheetTitle { get; init; } = string.Empty;

    public string PlatformLabel { get; init; } = string.Empty;

    public int Points { get; init; }

    public IReadOnlyList<DsSolverRowViewModel> Solvers { get; init; } = [];

    public IReadOnlyList<DsMemberOption> Members { get; init; } = [];

    /// <summary><c>datetime-local</c> value for the manual credit form.</summary>
    public string DefaultSolvedAtLocal { get; init; } = string.Empty;

    public string ProblemUrl { get; init; } = string.Empty;
}

public sealed record DsSolverRowViewModel(
    Guid UserId,
    string MemberName,
    string SolvedLabel,
    string LanguageLabel,
    int Points,
    bool IsLate,
    bool IsManual,
    string PlatformUrl);

public sealed record DsMemberOption(Guid Id, string Label);