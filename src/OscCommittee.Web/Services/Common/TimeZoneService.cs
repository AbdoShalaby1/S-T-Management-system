using System.Globalization;
using Microsoft.Extensions.Options;

namespace OscCommittee.Web.Services.Common;

public sealed class TimeZoneService : ITimeZoneService
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    private readonly TimeProvider _timeProvider;

    public TimeZoneService(TimeProvider timeProvider, IOptions<AppOptions> options, ILogger<TimeZoneService> logger)
    {
        _timeProvider = timeProvider;

        var id = options.Value.TimeZone;
        try
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Configured time zone {TimeZoneId} was not found. Falling back to UTC.", id);
            TimeZone = TimeZoneInfo.Utc;
        }
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, TimeZone);

    public DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZone.GetUtcOffset(unspecified));
    }

    public DateTimeOffset NowLocal() => ToLocal(UtcNow);

    public string CurrentMonthLabel() => FormatMonth(NowLocal());

    public string MonthLabel(DateTimeOffset utc) => FormatMonth(ToLocal(utc));

    public string FormatLocal(DateTimeOffset utc, string format) =>
        ToLocal(utc).ToString(format, Culture);

    private static string FormatMonth(DateTimeOffset local) => local.ToString("MMMM yyyy", Culture);
}
