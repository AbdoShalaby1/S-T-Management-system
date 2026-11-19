using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Integrations.LeetCode;

public sealed record LeetCodeProblemMetadata(
    string Slug,
    string Title,
    string? FrontendId,
    string? DifficultyLabel,
    bool IsPaidOnly,
    IReadOnlyList<string> Tags);

/// <summary>One accepted submission from the public twenty-row window.</summary>
public sealed record LeetCodeSubmission(
    string SubmissionId,
    string Slug,
    string Title,
    DateTimeOffset SubmittedAtUtc,
    string? Language);

public interface ILeetCodeClient
{
    Task<Result<LeetCodeProblemMetadata>> GetProblemAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>The recent accepted submissions, newest first. Publicly capped at twenty rows.</summary>
    Task<Result<IReadOnlyList<LeetCodeSubmission>>> GetRecentAcceptedAsync(
        string username,
        int limit,
        CancellationToken cancellationToken = default);

    Task<Result<PlatformHandle>> CheckHandleAsync(
        string username,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Public GraphQL metadata lookup (PLAN §10.1). No auth: the same query the site itself uses.
/// Only metadata is requested — the statement is never fetched.
/// </summary>
public sealed class LeetCodeClient(HttpClient httpClient, ILogger<LeetCodeClient> logger) : ILeetCodeClient
{
    public const string HttpClientName = "leetcode";

    private const string Query = """
        query($t: String!) {
          question(titleSlug: $t) {
            questionFrontendId
            title
            titleSlug
            difficulty
            isPaidOnly
            topicTags { name slug }
          }
        }
        """;

    private const string SubmissionsQuery = """
        query($u: String!, $l: Int!) {
          recentAcSubmissionList(username: $u, limit: $l) {
            id
            title
            titleSlug
            timestamp
            statusDisplay
            lang
          }
        }
        """;

    private const string UserQuery = """
        query($u: String!) {
          matchedUser(username: $u) {
            username
            profile { realName userAvatar }
          }
        }
        """;

    /// <summary>403/429 plus Cloudflare's HTML challenge page: back off, never retry tightly.</summary>
    private static bool IsBlocked(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
        || response.Headers.Contains("cf-mitigated")
        || (response.IsSuccessStatusCode
            && response.Content.Headers.ContentType?.MediaType is { } media
            && !media.Contains("json", StringComparison.OrdinalIgnoreCase));

    public async Task<Result<LeetCodeProblemMetadata>> GetProblemAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "graphql",
                new { query = Query, variables = new { t = slug } },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("LeetCode metadata for {Slug} returned HTTP {Status}.", slug, (int)response.StatusCode);
                return Result.Fail<LeetCodeProblemMetadata>(
                    "leetcode_unavailable",
                    "LeetCode did not answer. Check the URL and try again in a moment.");
            }

            var payload = await response.Content.ReadFromJsonAsync<GraphQlResponse>(cancellationToken);
            var question = payload?.Data?.Question;

            if (question is null)
            {
                return Result.Fail<LeetCodeProblemMetadata>(
                    "not_found",
                    "LeetCode has no problem with that URL. Check the slug.");
            }

            var tags = question.TopicTags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
                .Select(tag => tag.Name!.Trim())
                .ToList() ?? [];

            return Result.Ok(new LeetCodeProblemMetadata(
                question.TitleSlug ?? slug,
                question.Title ?? slug,
                question.QuestionFrontendId,
                question.Difficulty,
                question.IsPaidOnly,
                tags));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "LeetCode metadata lookup for {Slug} failed.", slug);
            return Result.Fail<LeetCodeProblemMetadata>(
                "leetcode_unavailable",
                "LeetCode could not be reached. You can still add the problem by typing its details.");
        }
    }

    public async Task<Result<IReadOnlyList<LeetCodeSubmission>>> GetRecentAcceptedAsync(
        string username,
        int limit,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "graphql",
                new { query = SubmissionsQuery, variables = new { u = username, l = Math.Clamp(limit, 1, 50) } },
                cancellationToken);

            if (IsBlocked(response))
            {
                logger.LogWarning(
                    "LeetCode submissions for {Handle} were blocked with HTTP {Status}.",
                    username,
                    (int)response.StatusCode);

                return Result.Fail<IReadOnlyList<LeetCodeSubmission>>(
                    "rate_limited",
                    "LeetCode is rate limiting this server right now. Try again in a minute; the last successful sync is kept.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "LeetCode submissions for {Handle} returned HTTP {Status}.",
                    username,
                    (int)response.StatusCode);

                return Result.Fail<IReadOnlyList<LeetCodeSubmission>>(
                    "leetcode_unavailable",
                    "LeetCode did not answer. Try again in a moment.");
            }

            var payload = await response.Content.ReadFromJsonAsync<SubmissionsResponse>(cancellationToken);
            var rows = payload?.Data?.RecentAcSubmissionList;

            if (rows is null)
            {
                return Result.Fail<IReadOnlyList<LeetCodeSubmission>>(
                    "not_found",
                    $"LeetCode has no public submissions for “{username}”. Check the handle in Settings.");
            }

            var submissions = new List<LeetCodeSubmission>(rows.Count);

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Id)
                    || string.IsNullOrWhiteSpace(row.TitleSlug)
                    || !long.TryParse(row.Timestamp, out var seconds))
                {
                    continue;
                }

                submissions.Add(new LeetCodeSubmission(
                    row.Id,
                    row.TitleSlug,
                    string.IsNullOrWhiteSpace(row.Title) ? row.TitleSlug : row.Title,
                    DateTimeOffset.FromUnixTimeSeconds(seconds),
                    string.IsNullOrWhiteSpace(row.Lang) ? null : row.Lang));
            }

            return Result.Ok<IReadOnlyList<LeetCodeSubmission>>(submissions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "LeetCode submission sync for {Handle} failed.", username);
            return Result.Fail<IReadOnlyList<LeetCodeSubmission>>(
                "leetcode_unavailable",
                "LeetCode could not be reached. Try again in a moment.");
        }
    }

    public async Task<Result<PlatformHandle>> CheckHandleAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "graphql",
                new { query = UserQuery, variables = new { u = username } },
                cancellationToken);

            if (IsBlocked(response))
            {
                return Result.Fail<PlatformHandle>(
                    "rate_limited",
                    "LeetCode is rate limiting this server right now. Try again in a minute.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<PlatformHandle>(
                    "leetcode_unavailable",
                    "LeetCode did not answer the check. Try again in a moment.");
            }

            var payload = await response.Content.ReadFromJsonAsync<UserResponse>(cancellationToken);
            var user = payload?.Data?.MatchedUser;

            if (user is null)
            {
                return Result.Fail<PlatformHandle>(
                    "not_found",
                    $"LeetCode has no user called “{username}”.");
            }

            return Result.Ok(new PlatformHandle(
                user.Username ?? username,
                string.IsNullOrWhiteSpace(user.Profile?.RealName) ? null : user.Profile!.RealName));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "LeetCode handle check for {Handle} failed.", username);
            return Result.Fail<PlatformHandle>(
                "leetcode_unavailable",
                "LeetCode could not be reached. Try again in a moment.");
        }
    }

    private sealed class GraphQlResponse
    {
        [JsonPropertyName("data")]
        public GraphQlData? Data { get; set; }
    }

    private sealed class GraphQlData
    {
        [JsonPropertyName("question")]
        public Question? Question { get; set; }
    }

    private sealed class SubmissionsResponse
    {
        [JsonPropertyName("data")]
        public SubmissionsData? Data { get; set; }
    }

    private sealed class SubmissionsData
    {
        [JsonPropertyName("recentAcSubmissionList")]
        public List<Submission>? RecentAcSubmissionList { get; set; }
    }

    private sealed class Submission
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("titleSlug")]
        public string? TitleSlug { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("statusDisplay")]
        public string? StatusDisplay { get; set; }

        [JsonPropertyName("lang")]
        public string? Lang { get; set; }
    }

    private sealed class UserResponse
    {
        [JsonPropertyName("data")]
        public UserData? Data { get; set; }
    }

    private sealed class UserData
    {
        [JsonPropertyName("matchedUser")]
        public MatchedUser? MatchedUser { get; set; }
    }

    private sealed class MatchedUser
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("profile")]
        public MatchedProfile? Profile { get; set; }
    }

    private sealed class MatchedProfile
    {
        [JsonPropertyName("realName")]
        public string? RealName { get; set; }
    }

    private sealed class Question
    {
        [JsonPropertyName("questionFrontendId")]
        public string? QuestionFrontendId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("titleSlug")]
        public string? TitleSlug { get; set; }

        [JsonPropertyName("difficulty")]
        public string? Difficulty { get; set; }

        [JsonPropertyName("isPaidOnly")]
        public bool IsPaidOnly { get; set; }

        [JsonPropertyName("topicTags")]
        public List<TopicTag>? TopicTags { get; set; }
    }

    private sealed class TopicTag
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }
}