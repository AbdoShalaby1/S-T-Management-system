using System.ComponentModel.DataAnnotations;
using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.ViewModels.Settings;

/// <summary>The editable half of <c>/settings</c> (PLAN §7.6).</summary>
public sealed class ProfileFormViewModel
{
    [Required(ErrorMessage = "Enter a display name.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Use between 2 and 80 characters.")]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(64, ErrorMessage = "Handles cannot be longer than 64 characters.")]
    [Display(Name = "LeetCode handle")]
    public string? LeetCodeHandle { get; set; }

    [StringLength(64, ErrorMessage = "Handles cannot be longer than 64 characters.")]
    [Display(Name = "Codeforces handle")]
    public string? CodeforcesHandle { get; set; }

    public DateTimeOffset? LeetCodeVerifiedAtUtc { get; set; }

    public DateTimeOffset? CodeforcesVerifiedAtUtc { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*[0-9]).*$", ErrorMessage = "Include at least one letter and one digit.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repeat the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class SettingsViewModel
{
    public ProfileFormViewModel Profile { get; init; } = new();

    public ChangePasswordViewModel Password { get; init; } = new();

    public string Email { get; init; } = string.Empty;

    public UserStatus Status { get; init; }

    public string StatusLabel => Board.UserStatusView.Label(Status);

    public string StatusBadgeClass => Board.UserStatusView.BadgeClass(Status);

    public string Initials { get; init; } = string.Empty;

    public string MemberSinceLabel { get; init; } = string.Empty;

    public string MonthLabel { get; init; } = string.Empty;

    public bool IsBoard { get; init; }

    /// <summary>Total for the open month, or an em dash when no month is open.</summary>
    public string CurrentMonthPointsLabel { get; init; } = "—";
}
