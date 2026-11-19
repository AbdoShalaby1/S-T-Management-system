using System.Collections.Concurrent;

namespace OscCommittee.Web.Services.Ds;

/// <summary>
/// Process-wide single-flight gate for member syncs (PLAN §10.1 step 2). A second request for the
/// same member and platform gets <c>sync_in_progress</c> instead of starting a parallel walk.
/// </summary>
public sealed class DsSyncGate
{
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public bool TryBegin(Guid userId, Domain.Enums.SyncPlatform platform) =>
        _inFlight.TryAdd(Key(userId, platform), 0);

    public void End(Guid userId, Domain.Enums.SyncPlatform platform) =>
        _inFlight.TryRemove(Key(userId, platform), out _);

    public bool IsInProgress(Guid userId, Domain.Enums.SyncPlatform platform) =>
        _inFlight.ContainsKey(Key(userId, platform));

    private static string Key(Guid userId, Domain.Enums.SyncPlatform platform) =>
        $"{userId:N}:{(int)platform}";
}