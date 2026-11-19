using Microsoft.Extensions.Options;

namespace OscCommittee.Web.Integrations.Codeforces;

/// <summary>
/// Process-wide gate for every Codeforces request (PLAN §10.2): at least
/// <see cref="SyncOptions.CodeforcesMinIntervalMs"/> between calls, no matter which user or
/// background job triggered them. Codeforces answers 429 and then bans IPs when this is ignored.
/// </summary>
public sealed class CodeforcesThrottle(IOptions<SyncOptions> options, TimeProvider timeProvider)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastCallUtc = DateTimeOffset.MinValue;

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var minimum = TimeSpan.FromMilliseconds(Math.Max(0, options.Value.CodeforcesMinIntervalMs));
            var wait = _lastCallUtc + minimum - timeProvider.GetUtcNow();

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, timeProvider, cancellationToken);
            }

            // Start the interval when the request leaves, not when it returns.
            _lastCallUtc = timeProvider.GetUtcNow();
            return await call(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}