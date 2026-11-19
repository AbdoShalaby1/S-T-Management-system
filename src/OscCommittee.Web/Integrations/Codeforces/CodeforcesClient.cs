using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Integrations.Codeforces;

/// <summary>One submission row from <c>user.status</c>.</summary>
public sealed record CodeforcesSubmission(
    long SubmissionId,
    int ContestId,
    string Index,
    string ProblemName,
    string? Verdict,
    DateTimeOffset SubmittedAtUtc,
    string? Language,
    string? ProblemType)
{
    public string ExternalId => $"{ContestId}:{Index}";

    public bool IsAccepted =>
        string.Equals(Verdict, "OK", StringComparison.Ordinal)
        && ProblemType is null or "PROGRAMMING";
}

public interface ICodeforcesClient
{
    /// <summary>Downloads the whole problemset. Rate limited by <see cref="CodeforcesThrottle"/>.</summary>
    Task<Result<IReadOnlyList<CodeforcesProblem>>> GetProblemsetAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The newest page of a member's submissions, newest first.</summary>
    Task<Result<IReadOnlyList<CodeforcesSubmission>>> GetSubmissionsAsync(
        string handle,
        int count,
        CancellationToken cancellationToken = default);

    Task<Result<PlatformHandle>> CheckHandleAsync(
        string handle,
        CancellationToken cancellationToken = default);
}

/// <summary>Codeforces metadata via the public API (PLAN §10.2). No auth, one request every 2 s.</summary>
public sealed class CodeforcesClient(
    HttpClient httpClient,
    CodeforcesThrottle throttle,
    ILogger<CodeforcesClient> logger) : ICodeforcesClient
{
    public const string HttpClientName = "codeforces";

    public Task<Result<IReadOnlyList<CodeforcesProblem>>> GetProblemsetAsync(
        CancellationToken cancellationToken = default) =>
        throttle.ExecuteAsync(async token =>
        {
            try
            {
                using var response = await httpClient.GetAsync("api/problemset.problems", token);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Codeforces problemset returned HTTP {Status}.", (int)response.StatusCode);
                    return Result.Fail<IReadOnlyList<CodeforcesProblem>>(
                        "codeforces_unavailable",
                        "Codeforces did not answer. You can still add the problem by typing its details.");
                }

                var payload = await response.Content.ReadFromJsonAsync<ProblemsetResponse>(token);

                if (payload?.Status != "OK" || payload.Result?.Problems is null)
                {
                    logger.LogWarning("Codeforces problemset returned status {Status}.", payload?.Status ?? "null");
                    return Result.Fail<IReadOnlyList<CodeforcesProblem>>(
                        "codeforces_unavailable",
                        "Codeforces returned an unexpected response.");
                }

                var problems = payload.Result.Problems
                    .Where(problem => problem.Type is null or "PROGRAMMING")
                    .Select(problem => new CodeforcesProblem(
                        problem.ContestId,
                        problem.Index?.ToUpperInvariant() ?? string.Empty,
                        problem.Name ?? string.Empty,
                        problem.Rating,
                        problem.Tags ?? []))
                    .Where(problem => problem.ContestId > 0 && problem.Index.Length > 0)
                    .ToList();

                logger.LogInformation("Loaded {Count} Codeforces problems.", problems.Count);
                return Result.Ok<IReadOnlyList<CodeforcesProblem>>(problems);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Codeforces problemset download failed.");
                return Result.Fail<IReadOnlyList<CodeforcesProblem>>(
                    "codeforces_unavailable",
                    "Codeforces could not be reached. You can still add the problem by typing its details.");
            }
        }, cancellationToken);

    public Task<Result<IReadOnlyList<CodeforcesSubmission>>> GetSubmissionsAsync(
        string handle,
        int count,
        CancellationToken cancellationToken = default) =>
        throttle.ExecuteAsync(async token =>
        {
            try
            {
                var url = $"api/user.status?handle={Uri.EscapeDataString(handle)}&from=1&count={Math.Clamp(count, 1, 10000)}";
                using var response = await httpClient.GetAsync(url, token);

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                    || (response.Content.Headers.ContentType?.MediaType is { } media
                        && !media.Contains("json", StringComparison.OrdinalIgnoreCase)))
                {
                    logger.LogWarning(
                        "Codeforces submissions for {Handle} were blocked with HTTP {Status}.",
                        handle,
                        (int)response.StatusCode);

                    return Result.Fail<IReadOnlyList<CodeforcesSubmission>>(
                        "rate_limited",
                        "Codeforces is rate limiting this server right now. Try again in a minute; the last successful sync is kept.");
                }

                var payload = await response.Content.ReadFromJsonAsync<SubmissionsResponse>(token);

                if (payload?.Status != "OK" || payload.Result is null)
                {
                    logger.LogWarning(
                        "Codeforces submissions for {Handle} returned {Status}: {Comment}",
                        handle,
                        payload?.Status ?? "null",
                        payload?.Comment);

                    return payload?.Comment?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                        ? Result.Fail<IReadOnlyList<CodeforcesSubmission>>(
                            "not_found",
                            $"Codeforces has no user called “{handle}”. Check the handle in Settings.")
                        : Result.Fail<IReadOnlyList<CodeforcesSubmission>>(
                            "codeforces_unavailable",
                            "Codeforces returned an unexpected response. Try again in a moment.");
                }

                var submissions = payload.Result
                    .Where(row => row.Problem?.Index is { Length: > 0 } && row.ContestId > 0)
                    .Select(row => new CodeforcesSubmission(
                        row.Id,
                        row.ContestId,
                        row.Problem!.Index!.ToUpperInvariant(),
                        row.Problem.Name ?? row.Problem.Index!,
                        row.Verdict,
                        DateTimeOffset.FromUnixTimeSeconds(row.CreationTimeSeconds),
                        string.IsNullOrWhiteSpace(row.ProgrammingLanguage) ? null : row.ProgrammingLanguage,
                        row.Problem.Type))
                    .ToList();

                logger.LogInformation(
                    "Loaded {Count} Codeforces submissions for {Handle}.",
                    submissions.Count,
                    handle);

                return Result.Ok<IReadOnlyList<CodeforcesSubmission>>(submissions);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Codeforces submission sync for {Handle} failed.", handle);
                return Result.Fail<IReadOnlyList<CodeforcesSubmission>>(
                    "codeforces_unavailable",
                    "Codeforces could not be reached. Try again in a moment.");
            }
        }, cancellationToken);

    public Task<Result<PlatformHandle>> CheckHandleAsync(
        string handle,
        CancellationToken cancellationToken = default) =>
        throttle.ExecuteAsync(async token =>
        {
            try
            {
                using var response = await httpClient.GetAsync($"api/user.info?handles={Uri.EscapeDataString(handle)}", token);

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    return Result.Fail<PlatformHandle>(
                        "rate_limited",
                        "Codeforces is rate limiting this server right now. Try again in a minute.");
                }

                var payload = await response.Content.ReadFromJsonAsync<UserInfoResponse>(token);
                var user = payload?.Result?.FirstOrDefault();

                if (payload?.Status != "OK" || user is null)
                {
                    return Result.Fail<PlatformHandle>(
                        "not_found",
                        $"Codeforces has no user called “{handle}”.");
                }

                var name = string.Join(
                    ' ',
                    new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

                return Result.Ok(new PlatformHandle(
                    user.Handle ?? handle,
                    name.Length == 0 ? null : name));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Codeforces handle check for {Handle} failed.", handle);
                return Result.Fail<PlatformHandle>(
                    "codeforces_unavailable",
                    "Codeforces could not be reached. Try again in a moment.");
            }
        }, cancellationToken);

    private sealed class ProblemsetResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("result")]
        public ProblemsetResult? Result { get; set; }
    }

    private sealed class ProblemsetResult
    {
        [JsonPropertyName("problems")]
        public List<Problem>? Problems { get; set; }
    }

    private sealed class Problem
    {
        [JsonPropertyName("contestId")]
        public int ContestId { get; set; }

        [JsonPropertyName("index")]
        public string? Index { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("rating")]
        public int? Rating { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }

    private sealed class SubmissionsResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("result")]
        public List<SubmissionRow>? Result { get; set; }
    }

    private sealed class SubmissionRow
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("contestId")]
        public int ContestId { get; set; }

        [JsonPropertyName("creationTimeSeconds")]
        public long CreationTimeSeconds { get; set; }

        [JsonPropertyName("programmingLanguage")]
        public string? ProgrammingLanguage { get; set; }

        [JsonPropertyName("verdict")]
        public string? Verdict { get; set; }

        [JsonPropertyName("problem")]
        public SubmissionProblem? Problem { get; set; }
    }

    private sealed class SubmissionProblem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("index")]
        public string? Index { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private sealed class UserInfoResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("result")]
        public List<UserInfoRow>? Result { get; set; }
    }

    private sealed class UserInfoRow
    {
        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }
    }
}