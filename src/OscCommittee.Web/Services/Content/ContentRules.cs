using System.Text;
using System.Text.RegularExpressions;

namespace OscCommittee.Web.Services.Content;

/// <summary>Pure content-page rules: title/body size and image checks. Kept free of EF.</summary>
public static class ContentRules
{
    public const int MaxTitleLength = 160;

    public const int MinTitleLength = 3;

    /// <summary>Guard against a paste filling the database; the editor shows a counter.</summary>
    public const int MaxBodyChars = 200_000;

    private static readonly Regex RepeatedDash = new("-{2,}", RegexOptions.Compiled);

    /// <summary>Sanitises an uploaded file name; the extension is preserved by the caller.</summary>
    public static string SafeFileStem(string? fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty).ToLowerInvariant();
        var builder = new StringBuilder(stem.Length);

        foreach (var character in stem)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        var safe = RepeatedDash.Replace(builder.ToString(), "-").Trim('-');
        if (safe.Length == 0)
        {
            return "image";
        }

        return safe.Length <= 60 ? safe : safe[..60].TrimEnd('-');
    }

    /// <summary>Simple signature check so a renamed executable cannot land in the content folder.</summary>
    public static bool LooksLikeImage(string extension, ReadOnlySpan<byte> bytes) => extension switch
    {
        ".png" => bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
        ".jpg" or ".jpeg" => bytes.Length >= 3
            && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ".gif" => bytes.Length >= 6
            && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'8'
            && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9')
            && bytes[5] == (byte)'a',
        ".webp" => bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
        _ => false
    };
}