using Microsoft.Extensions.Options;

namespace OscCommittee.Web.Integrations.Codeforces;

/// <summary>
/// Keeps <see cref="CodeforcesProblemCache"/> warm without touching the network at startup
/// (PLAN §10.2): the first lookup loads the problemset on demand, and this service only refreshes
/// a copy that has already been loaded, every <see cref="SyncOptions.ProblemsetCacheHours"/>.
/// The download is ~2.3 MB, so it is never done per request.
/// </summary>
public sealed class CodeforcesCacheRefreshService(
    CodeforcesProblemCache cache,
    IOptions<SyncOptions> options,
    ILogger<CodeforcesCacheRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.ProblemsetCacheHours));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Nobody has used the cache yet, so stay quiet instead of downloading unprompted.
                if (!cache.Status.IsLoaded)
                {
                    continue;
                }

                try
                {
                    await cache.RefreshAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Scheduled Codeforces problemset refresh failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}