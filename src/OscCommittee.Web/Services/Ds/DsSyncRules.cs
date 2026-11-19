using OscCommittee.Web.Domain.Enums;

namespace OscCommittee.Web.Services.Ds;

/// <summary>
/// The rules from PLAN §6.3–§6.5 and §10 that are worth testing without a database or network:
/// the assignment cutoff, late detection, completion month and the history-gap flag.
/// </summary>
public static class DsSyncRules
{
    /// <summary>LeetCode's public accepted-submission window is hard-capped at twenty rows.</summary>
    public const int HistoryWindow = 20;

    /// <summary>Only submissions at or after the problem's assignment can credit points.</summary>
    public static bool IsAfterAssignment(DateTimeOffset assignedAtUtc, DateTimeOffset solvedAtUtc) =>
        solvedAtUtc >= assignedAtUtc;

    /// <summary>Accepted after the sheet deadline: recorded, flagged, worth zero.</summary>
    public static bool IsLate(DateTimeOffset solvedAtUtc, DateTimeOffset dueAtUtc) =>
        solvedAtUtc > dueAtUtc;

    /// <summary>
    /// Points land in the month the solve happened in, in the app time zone — never the sheet's
    /// month (a November sheet may be due in December).
    /// </summary>
    public static (int Year, int Month) CompletionMonth(DateTimeOffset solvedAtUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(solvedAtUtc, timeZone);
        return (local.Year, local.Month);
    }

    /// <summary>A full page means older accepted submissions may already be out of view.</summary>
    public static bool HistoryGapPossible(int returnedRows, int window = HistoryWindow) => returnedRows >= window;

    /// <summary>LeetCode slugs compare case-insensitively; Codeforces indexes are upper case.</summary>
    public static string NormalizeExternalId(Platform platform, string externalId)
    {
        var trimmed = externalId.Trim();

        if (platform == Platform.LeetCode)
        {
            return trimmed.ToLowerInvariant();
        }

        var parts = trimmed.Split(':');
        return parts.Length == 2
            ? $"{parts[0]}:{parts[1].ToUpperInvariant()}"
            : trimmed.ToUpperInvariant();
    }

    /// <summary>Key used for the in-memory "already credited" set during a sync.</summary>
    public static string SolveKey(Platform platform, string externalId) =>
        $"{(int)platform}:{NormalizeExternalId(platform, externalId)}";

    /// <summary>Submission page on the platform, or the problem page when none exists.</summary>
    public static string SubmissionUrl(Platform platform, string externalId, string submissionId)
    {
        if (platform == Platform.LeetCode)
        {
            return long.TryParse(submissionId, out _)
                ? $"https://leetcode.com/submissions/detail/{submissionId}/"
                : $"https://leetcode.com/problems/{NormalizeExternalId(platform, externalId)}/";
        }

        var parts = NormalizeExternalId(platform, externalId).Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], out var contestId)
            && long.TryParse(submissionId, out _)
            ? $"https://codeforces.com/contest/{contestId}/submission/{submissionId}"
            : parts.Length == 2
                ? $"https://codeforces.com/problemset/problem/{parts[0]}/{parts[1]}"
                : $"https://codeforces.com/problemset";
    }
}