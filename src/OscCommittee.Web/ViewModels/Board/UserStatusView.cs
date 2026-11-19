using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.ViewModels.Board;

/// <summary>Status wording and badge colour, kept in one place so every list agrees.</summary>
public static class UserStatusView
{
    public static string Label(UserStatus status) => status switch
    {
        UserStatus.Active => "Active",
        UserStatus.Pending => "Pending",
        UserStatus.Rejected => "Rejected",
        UserStatus.Deactivated => "Deactivated",
        _ => status.ToString()
    };

    public static string BadgeClass(UserStatus status) => status switch
    {
        UserStatus.Active => "badge-active",
        UserStatus.Pending => "badge-late",
        UserStatus.Rejected => "badge-hard",
        _ => "badge-hidden"
    };

    /// <summary>Cheap initials for the avatar circle: first letters of the first two words.</summary>
    public static string Initials(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0
            ? "?"
            : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}
