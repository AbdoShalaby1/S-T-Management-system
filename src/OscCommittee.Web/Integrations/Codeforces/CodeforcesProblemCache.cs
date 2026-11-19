using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Integrations.Codeforces;

/// <summary>
/// In-memory copy of <c>problemset.problems</c> (PLAN §10.2): loaded on the first lookup, refreshed
/// every <see cref="SyncOptions.ProblemsetCacheHours"/>, and refreshed on demand when a lookup misses.
/// A miss only triggers a download when the copy is more than ten minutes old, so a typo in a URL
/// cannot turn into a request storm.
/// </summary>
public sealed class CodeforcesProblemCache(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    TimeProvider timeProvider,
    ILogger<CodeforcesProblemCache> logger)
{
    private static readonly TimeSpan MissRefreshFloor = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Snapshot? _snapshot;

    /// <summary>Diagnostic view for the board (count and age of the cached problemset).</summary>
    public CodeforcesCacheStatus Status
    {
        get
        {
            var snapshot = _snapshot;
            return snapshot is null
                ? new CodeforcesCacheStatus(false, 0, null)
                : new CodeforcesCacheStatus(true, snapshot.Problems.Count, snapshot.LoadedAtUtc);
        }
    }

    public async Task<CodeforcesProblem?> FindAsync(
        int contestId,
        string index,
        CancellationToken cancellationToken = default)
    {
        var key = $"{contestId}:{index.ToUpperInvariant()}";
        var snapshot = _snapshot;

        if (snapshot is null || IsStale(snapshot))
        {
            snapshot = await LoadAsync(force: false, cancellationToken) ?? snapshot;
        }

        if (snapshot is not null && snapshot.Problems.TryGetValue(key, out var problem))
        {
            return problem;
        }

        // Unknown id: the cache may predate the contest. One guarded retry, then give up.
        if (snapshot is null || timeProvider.GetUtcNow() - snapshot.LoadedAtUtc >= MissRefreshFloor)
        {
            snapshot = await LoadAsync(force: true, cancellationToken) ?? snapshot;
        }

        return snapshot is not null && snapshot.Problems.TryGetValue(key, out problem) ? problem : null;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(force: true, cancellationToken);

    private bool IsStale(Snapshot snapshot) =>
        timeProvider.GetUtcNow() - snapshot.LoadedAtUtc
            >= TimeSpan.FromHours(Math.Max(1, options.Value.ProblemsetCacheHours));

    private async Task<Snapshot?> LoadAsync(bool force, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            var existing = _snapshot;

            if (!force && existing is not null && !IsStale(existing))
            {
                return existing;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<ICodeforcesClient>();

            var result = await client.GetProblemsetAsync(cancellationToken);
            if (result.Failed || result.Value is null)
            {
                // Keep the previous copy: stale metadata beats no metadata.
                logger.LogWarning("Codeforces problemset refresh failed: {Message}", result.Message);
                return existing;
            }

            var problems = result.Value.ToDictionary(
                problem => problem.ExternalId,
                problem => problem,
                StringComparer.Ordinal);

            var snapshot = new Snapshot(problems, timeProvider.GetUtcNow());
            _snapshot = snapshot;

            logger.LogInformation(
                "Codeforces problemset cached: {Count} problems at {LoadedAt:O}.",
                problems.Count,
                snapshot.LoadedAtUtc);

            return snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<string, CodeforcesProblem> Problems,
        DateTimeOffset LoadedAtUtc);
}

public sealed record CodeforcesCacheStatus(bool IsLoaded, int ProblemCount, DateTimeOffset? LoadedAtUtc);