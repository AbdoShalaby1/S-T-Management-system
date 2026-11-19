using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Ds;

/// <summary>A pasted URL resolved to a platform id and its canonical form.</summary>
public sealed record DsProblemLink(Platform Platform, string ExternalId, string CanonicalUrl);

/// <summary>
/// Parses the URL shapes from PLAN §8.5. Kept free of I/O so the rules are unit-testable and the
/// board gets an actionable message instead of a raw fetch failure.
/// </summary>
public static class DsProblemUrl
{
    public const string InvalidMessage =
        "Paste a LeetCode problem URL (leetcode.com/problems/…) or a Codeforces problem URL (codeforces.com/problemset/problem/…).";

    public static Result<DsProblemLink> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Fail<DsProblemLink>("invalid_url", InvalidMessage);
        }

        var text = raw.Trim();

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Fail<DsProblemLink>("invalid_url", InvalidMessage);
        }

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return host switch
        {
            "leetcode.com" or "www.leetcode.com" => ParseLeetCode(segments),
            "codeforces.com" or "www.codeforces.com" or "m.codeforces.com" => ParseCodeforces(segments),
            _ => Result.Fail<DsProblemLink>("unsupported_site", InvalidMessage)
        };
    }

    /// <summary>
    /// Rebuilds the public URL from the stored id, so a link can never drift from the problem the
    /// sync matches on.
    /// </summary>
    public static string Canonical(Platform platform, string externalId)
    {
        if (platform == Platform.LeetCode)
        {
            return $"https://leetcode.com/problems/{externalId.Trim().ToLowerInvariant()}/";
        }

        var parts = externalId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out var contestId)
            ? $"https://codeforces.com/problemset/problem/{contestId}/{parts[1].ToUpperInvariant()}"
            : string.Empty;
    }

    /// <summary><c>/problems/{slug}</c>, optionally followed by <c>/description</c> or a subpath.</summary>
    private static Result<DsProblemLink> ParseLeetCode(string[] segments)
    {
        if (segments.Length < 2
            || !segments[0].Equals("problems", StringComparison.OrdinalIgnoreCase)
            || segments[1].Equals("problemset", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<DsProblemLink>("unsupported_url", InvalidMessage);
        }

        var slug = Uri.UnescapeDataString(segments[1]).Trim().ToLowerInvariant();

        if (slug.Length is < 1 or > 64 || !slug.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            return Result.Fail<DsProblemLink>("unsupported_url", InvalidMessage);
        }

        return Result.Ok(new DsProblemLink(
            Platform.LeetCode,
            slug,
            $"https://leetcode.com/problems/{slug}/"));
    }

    /// <summary><c>/problemset/problem/{contestId}/{index}</c> or <c>/contest/{id}/problem/{index}</c>.</summary>
    private static Result<DsProblemLink> ParseCodeforces(string[] segments)
    {
        string? contestId = null;
        string? index = null;

        if (segments.Length >= 4
            && segments[0].Equals("problemset", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("problem", StringComparison.OrdinalIgnoreCase))
        {
            contestId = segments[2];
            index = segments[3];
        }
        else if (segments.Length >= 4
            && segments[0].Equals("contest", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("problem", StringComparison.OrdinalIgnoreCase))
        {
            contestId = segments[1];
            index = segments[3];
        }

        if (contestId is null || index is null
            || !int.TryParse(contestId, out var numericId) || numericId <= 0
            || index.Length is < 1 or > 4
            || !index.All(char.IsAsciiLetterOrDigit))
        {
            return Result.Fail<DsProblemLink>("unsupported_url", InvalidMessage);
        }

        var normalizedIndex = index.ToUpperInvariant();

        return Result.Ok(new DsProblemLink(
            Platform.Codeforces,
            $"{numericId}:{normalizedIndex}",
            $"https://codeforces.com/problemset/problem/{numericId}/{normalizedIndex}"));
    }
}