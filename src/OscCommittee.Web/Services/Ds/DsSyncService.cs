using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Integrations.Codeforces;
using OscCommittee.Web.Integrations.LeetCode;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Ds;

/// <summary>
/// The sync engine from PLAN §10.1/§10.2. Each platform re-reads its recent submission feed,
/// credits every sheet copy of a problem assigned before the submission, dedupes per problem
/// through <see cref="IDsCreditService"/>, and reports the summary.
/// </summary>
public sealed class DsSyncService(
    AppDbContext db,
    IDsCreditService credit,
    ILeetCodeClient leetCode,
    ICodeforcesClient codeforces,
    DsSyncGate gate,
    TimeProvider timeProvider,
    ILogger<DsSyncService> logger) : IDsSyncService
{
    /// <summary>Codeforces caps a single <c>user.status</c> page at ten thousand rows.</summary>
    private const int CodeforcesPageSize = 10000;

    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)];

    public async Task<DsSyncStatus> GetStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var handles = await LoadHandlesAsync(userId, cancellationToken);
        if (handles is null)
        {
            return new DsSyncStatus([]);
        }

        var states = await db.SyncStates
            .AsNoTracking()
            .Where(state => state.UserId == userId)
            .ToDictionaryAsync(state => state.Platform, cancellationToken);

        return new DsSyncStatus(
        [
            BuildStatus(SyncPlatform.LeetCode, handles.LeetCodeHandle, states.GetValueOrDefault(SyncPlatform.LeetCode), gate.IsInProgress(userId, SyncPlatform.LeetCode)),
            BuildStatus(SyncPlatform.Codeforces, handles.CodeforcesHandle, states.GetValueOrDefault(SyncPlatform.Codeforces), gate.IsInProgress(userId, SyncPlatform.Codeforces))
        ]);
    }

    public async Task<DsSyncResult> SyncAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var handles = await LoadHandlesAsync(userId, cancellationToken);
        if (handles is null)
        {
            return DsSyncResult.Fail("not_found", "That account no longer exists.");
        }

        var platforms = new List<SyncPlatform>(2);
        if (!string.IsNullOrWhiteSpace(handles.LeetCodeHandle))
        {
            platforms.Add(SyncPlatform.LeetCode);
        }

        if (!string.IsNullOrWhiteSpace(handles.CodeforcesHandle))
        {
            platforms.Add(SyncPlatform.Codeforces);
        }

        if (platforms.Count == 0)
        {
            return DsSyncResult.Fail(
                "needs_handle",
                "Add a LeetCode or Codeforces handle in Settings before syncing.");
        }

        var now = timeProvider.GetUtcNow();
        var states = await db.SyncStates
            .Where(state => state.UserId == userId)
            .ToDictionaryAsync(state => state.Platform, cancellationToken);

        var summaries = new List<DsPlatformSyncResult>(platforms.Count);
        var credited = new List<DsSyncProblemResult>();

        foreach (var platform in platforms)
        {
            if (!gate.TryBegin(userId, platform))
            {
                summaries.Add(new DsPlatformSyncResult(
                    platform,
                    false,
                    true,
                    "sync_in_progress",
                    "A sync for this platform is already running.",
                    0,
                    0,
                    false));

                continue;
            }

            try
            {
                var state = states.GetValueOrDefault(platform);

                var (summary, problems) = platform == SyncPlatform.LeetCode
                    ? await SyncLeetCodeAsync(userId, handles.LeetCodeHandle!, state, now, cancellationToken)
                    : await SyncCodeforcesAsync(userId, handles.CodeforcesHandle!, state, now, cancellationToken);

                summaries.Add(summary);
                credited.AddRange(problems);
            }
            finally
            {
                gate.End(userId, platform);
            }
        }

        var succeeded = summaries.Any(summary => summary.Succeeded);
        var errorCode = !succeeded
            ? summaries.Any(summary => summary.ErrorCode == "sync_in_progress")
                ? "sync_in_progress"
                : summaries.FirstOrDefault()?.ErrorCode ?? "sync_failed"
            : null;

        return new DsSyncResult(
            succeeded,
            errorCode,
            BuildMessage(summaries, succeeded),
            summaries,
            credited);
    }

    private async Task<(DsPlatformSyncResult, List<DsSyncProblemResult>)> SyncLeetCodeAsync(
        Guid userId,
        string handle,
        SyncState? state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var response = await leetCode.GetRecentAcceptedAsync(
            handle,
            DsSyncRules.HistoryWindow,
            cancellationToken);

        for (var attempt = 0; response.Failed && response.ErrorCode == "rate_limited" && attempt < Backoff.Length; attempt++)
        {
            logger.LogWarning(
                "LeetCode rate limited {Handle}; backing off {Delay} s.",
                handle,
                Backoff[attempt].TotalSeconds);

            await Task.Delay(Backoff[attempt], timeProvider, cancellationToken);
            response = await leetCode.GetRecentAcceptedAsync(
                handle,
                DsSyncRules.HistoryWindow,
                cancellationToken);
        }

        if (response.Failed || response.Value is null)
        {
            var code = response.ErrorCode ?? "leetcode_unavailable";
            await RecordFailureAsync(userId, SyncPlatform.LeetCode, state, code, response.Message, now, cancellationToken);
            return (Failure(SyncPlatform.LeetCode, code, response.Message), []);
        }

        var submissions = response.Value;
        var lookup = await LoadProblemsAsync(Platform.LeetCode, cancellationToken);
        var horizon = EarliestAssignment(lookup);
        var solved = await LoadSolvedProblemIdsAsync(userId, cancellationToken);
        var results = new List<DsSyncProblemResult>();
        var newSolves = 0;
        var points = 0;

        // The feed is a rolling twenty-row window: every sync re-reads all of it and the solve
        // table does the dedupe. There is no cursor, so a problem the board adds after a member
        // has already solved it still gets credited. A submission older than the earliest
        // assignment on the sheets cannot earn anything, so that is the only safe stop.
        foreach (var submission in submissions)
        {
            if (!long.TryParse(submission.SubmissionId, out _))
            {
                continue;
            }

            if (horizon is { } assignedFrom && submission.SubmittedAtUtc < assignedFrom)
            {
                // Older than every assignment on the sheets: it can never earn points.
                continue;
            }

            var key = DsSyncRules.SolveKey(Platform.LeetCode, submission.Slug);

            if (!lookup.TryGetValue(key, out var candidates))
            {
                continue;
            }

            // The same platform problem may sit on several sheets. Each copy is its own
            // assignment, and each one that was assigned before this submission gets credited.
            foreach (var problem in candidates.Where(candidate =>
                DsSyncRules.IsAfterAssignment(candidate.AssignedAtUtc, submission.SubmittedAtUtc)))
            {
                if (solved.Contains(problem.Id))
                {
                    continue;
                }

                var outcome = await credit.CreditAsync(
                    new DsCreditRequest(
                        userId,
                        problem.Id,
                        submission.SubmissionId,
                        submission.SubmittedAtUtc,
                        submission.Language,
                        DsSyncRules.SubmissionUrl(Platform.LeetCode, submission.Slug, submission.SubmissionId)),
                    cancellationToken);

                if (outcome.Credited)
                {
                    newSolves++;
                    points += outcome.Points;
                    solved.Add(problem.Id);
                    results.Add(new DsSyncProblemResult(problem.Title, "LeetCode", outcome.Points, outcome.IsLate));
                }
                else if (outcome.Duplicate)
                {
                    solved.Add(problem.Id);
                }
            }
        }

        var updated = EnsureState(userId, SyncPlatform.LeetCode, state);
        updated.HistoryGapPossible = DsSyncRules.HistoryGapPossible(submissions.Count);
        updated.FailureCount = 0;
        updated.LastError = null;
        updated.LastSyncAtUtc = now;
        updated.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        return (new DsPlatformSyncResult(
            SyncPlatform.LeetCode,
            true,
            false,
            null,
            string.Empty,
            newSolves,
            points,
            updated.HistoryGapPossible), results);
    }

    private async Task<(DsPlatformSyncResult, List<DsSyncProblemResult>)> SyncCodeforcesAsync(
        Guid userId,
        string handle,
        SyncState? state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var response = await codeforces.GetSubmissionsAsync(handle, CodeforcesPageSize, cancellationToken);

        for (var attempt = 0; response.Failed && response.ErrorCode == "rate_limited" && attempt < Backoff.Length; attempt++)
        {
            logger.LogWarning(
                "Codeforces rate limited {Handle}; backing off {Delay} s.",
                handle,
                Backoff[attempt].TotalSeconds);

            await Task.Delay(Backoff[attempt], timeProvider, cancellationToken);
            response = await codeforces.GetSubmissionsAsync(handle, CodeforcesPageSize, cancellationToken);
        }

        if (response.Failed || response.Value is null)
        {
            var code = response.ErrorCode ?? "codeforces_unavailable";
            await RecordFailureAsync(userId, SyncPlatform.Codeforces, state, code, response.Message, now, cancellationToken);
            return (Failure(SyncPlatform.Codeforces, code, response.Message), []);
        }

        var submissions = response.Value;
        var lookup = await LoadProblemsAsync(Platform.Codeforces, cancellationToken);
        var horizon = EarliestAssignment(lookup);
        var solved = await LoadSolvedProblemIdsAsync(userId, cancellationToken);
        var results = new List<DsSyncProblemResult>();
        var newSolves = 0;
        var points = 0;

        // Same rule as LeetCode: no cursor, re-read the feed and let the solve table dedupe.
        // A submission older than the earliest assignment cannot credit anything.
        foreach (var submission in submissions)
        {
            if (horizon is { } assignedFrom && submission.SubmittedAtUtc < assignedFrom)
            {
                // Newest first: everything below the earliest assignment is out of scope.
                break;
            }

            if (!submission.IsAccepted)
            {
                continue;
            }

            var key = DsSyncRules.SolveKey(Platform.Codeforces, submission.ExternalId);

            if (!lookup.TryGetValue(key, out var candidates))
            {
                continue;
            }

            foreach (var problem in candidates.Where(candidate =>
                DsSyncRules.IsAfterAssignment(candidate.AssignedAtUtc, submission.SubmittedAtUtc)))
            {
                if (solved.Contains(problem.Id))
                {
                    continue;
                }

                var outcome = await credit.CreditAsync(
                    new DsCreditRequest(
                        userId,
                        problem.Id,
                        submission.SubmissionId.ToString(CultureInfo.InvariantCulture),
                        submission.SubmittedAtUtc,
                        submission.Language,
                        DsSyncRules.SubmissionUrl(
                            Platform.Codeforces,
                            submission.ExternalId,
                            submission.SubmissionId.ToString(CultureInfo.InvariantCulture))),
                    cancellationToken);

                if (outcome.Credited)
                {
                    newSolves++;
                    points += outcome.Points;
                    solved.Add(problem.Id);
                    results.Add(new DsSyncProblemResult(problem.Title, "Codeforces", outcome.Points, outcome.IsLate));
                }
                else if (outcome.Duplicate)
                {
                    solved.Add(problem.Id);
                }
            }
        }

        var updated = EnsureState(userId, SyncPlatform.Codeforces, state);
        updated.HistoryGapPossible = DsSyncRules.HistoryGapPossible(submissions.Count, CodeforcesPageSize);
        updated.FailureCount = 0;
        updated.LastError = null;
        updated.LastSyncAtUtc = now;
        updated.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        return (new DsPlatformSyncResult(
            SyncPlatform.Codeforces,
            true,
            false,
            null,
            string.Empty,
            newSolves,
            points,
            updated.HistoryGapPossible), results);
    }

    private async Task RecordFailureAsync(
        Guid userId,
        SyncPlatform platform,
        SyncState? state,
        string errorCode,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Sync for {UserId} on {Platform} failed ({Code}): {Message}",
            userId,
            platform,
            errorCode,
            message);

        var updated = EnsureState(userId, platform, state);
        updated.FailureCount += 1;
        updated.LastError = message.Length > 512 ? message[..512] : message;
        updated.UpdatedAtUtc = now;

        // Keep the cursor and the last successful timestamp; only the retry guard moves.
        await db.SaveChangesAsync(cancellationToken);
    }

    private SyncState EnsureState(Guid userId, SyncPlatform platform, SyncState? state)
    {
        if (state is not null)
        {
            return state;
        }

        var created = new SyncState
        {
            UserId = userId,
            Platform = platform,
            UpdatedAtUtc = timeProvider.GetUtcNow()
        };

        db.SyncStates.Add(created);
        return created;
    }

    private static DsPlatformStatus BuildStatus(
        SyncPlatform platform,
        string? handle,
        SyncState? state,
        bool inProgress = false)
    {
        var linked = !string.IsNullOrWhiteSpace(handle);

        return new DsPlatformStatus(
            platform,
            linked,
            state?.LastSyncAtUtc,
            state?.HistoryGapPossible ?? false,
            inProgress,
            state?.FailureCount ?? 0,
            state?.LastError);
    }

    private async Task<Dictionary<string, List<ProblemCandidate>>> LoadProblemsAsync(
        Platform platform,
        CancellationToken cancellationToken)
    {
        var rows = await db.DsProblems
            .AsNoTracking()
            .Where(problem => problem.Platform == platform && !problem.DsSheet!.IsHidden)
            .Select(problem => new
            {
                problem.Id,
                problem.Title,
                problem.ExternalId,
                problem.AssignedAtUtc,
                problem.Points
            })
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<string, List<ProblemCandidate>>(StringComparer.Ordinal);

        foreach (var row in rows
            .OrderBy(row => row.AssignedAtUtc)
            .ThenByDescending(row => row.Points)
            .ThenBy(row => row.Id))
        {
            var key = DsSyncRules.SolveKey(platform, row.ExternalId);

            if (!lookup.TryGetValue(key, out var list))
            {
                lookup[key] = list = [];
            }

            list.Add(new ProblemCandidate(row.Id, row.Title, row.AssignedAtUtc));
        }

        return lookup;
    }

    private async Task<HashSet<Guid>> LoadSolvedProblemIdsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var problemIds = await db.DsSolves
            .AsNoTracking()
            .Where(solve => solve.UserId == userId)
            .Select(solve => solve.DsProblemId)
            .ToListAsync(cancellationToken);

        return [.. problemIds];
    }

    private async Task<Handles?> LoadHandlesAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new Handles(user.LeetCodeHandle, user.CodeforcesHandle))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The earliest assignment among the visible sheet problems. A submission older than this
    /// cannot credit anything, so it is the only safe place to stop walking the feed.
    /// </summary>
    private static DateTimeOffset? EarliestAssignment(
        Dictionary<string, List<ProblemCandidate>> lookup) =>
        lookup.Count == 0
            ? null
            : lookup.Values
                .SelectMany(candidates => candidates)
                .Min(candidate => candidate.AssignedAtUtc);

    private static DsPlatformSyncResult Failure(SyncPlatform platform, string errorCode, string message) =>
        new(platform, false, false, errorCode, message, 0, 0, false);

    private static string BuildMessage(IReadOnlyList<DsPlatformSyncResult> summaries, bool succeeded)
    {
        var parts = new List<string>(summaries.Count);

        foreach (var summary in summaries)
        {
            var label = summary.Platform == SyncPlatform.LeetCode ? "LeetCode" : "Codeforces";

            if (!summary.Succeeded)
            {
                parts.Add($"{label}: {summary.Message}");
                continue;
            }

            parts.Add(summary.NewSolves > 0
                ? $"{label}: {summary.NewSolves} new solve{(summary.NewSolves == 1 ? string.Empty : "s")} ({summary.Points} pts)"
                : $"{label}: up to date");
        }

        if (parts.Count == 0)
        {
            return succeeded ? "Everything is up to date." : "Nothing was synced.";
        }

        return string.Join(" · ", parts);
    }

    private sealed record Handles(string? LeetCodeHandle, string? CodeforcesHandle);

    private sealed record ProblemCandidate(Guid Id, string Title, DateTimeOffset AssignedAtUtc);
}