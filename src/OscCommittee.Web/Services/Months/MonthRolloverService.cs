using OscCommittee.Web.Services.Common;

namespace OscCommittee.Web.Services.Months;

/// <summary>
/// Closes the previous month at 00:05 in the configured time zone (PLAN §6.1) and makes sure
/// the new month exists. Also catches up on startup if the app was down over a rollover.
/// </summary>
public sealed class MonthRolloverService(
    IServiceScopeFactory scopeFactory,
    ITimeZoneService timeZone,
    TimeProvider timeProvider,
    ILogger<MonthRolloverService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunAfter(timeZone.NowLocal()) - timeProvider.GetUtcNow();
            if (delay < TimeSpan.FromSeconds(30))
            {
                delay = TimeSpan.FromSeconds(30);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunAsync(stoppingToken);
        }
    }

    /// <summary>The next 00:05 on the first of a month, in the app time zone.</summary>
    public DateTimeOffset NextRunAfter(DateTimeOffset localNow)
    {
        var candidate = new DateTime(localNow.Year, localNow.Month, 1, 0, 5, 0);
        if (localNow.DateTime >= candidate)
        {
            candidate = candidate.AddMonths(1);
        }

        return new DateTimeOffset(candidate, timeZone.TimeZone.GetUtcOffset(candidate));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var months = scope.ServiceProvider.GetRequiredService<IMonthSheetService>();

        try
        {
            var closed = await months.CloseElapsedAsync(cancellationToken);
            var current = await months.GetOrCreateCurrentAsync(cancellationToken);

            if (closed > 0)
            {
                logger.LogInformation("Rollover closed {Count} month sheet(s).", closed);
            }

            logger.LogDebug(
                "Current month sheet is {Year}-{Month:00} ({Status}).",
                current.Year,
                current.Month,
                current.Status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Month rollover failed. It will retry on the next tick.");
        }
    }
}
