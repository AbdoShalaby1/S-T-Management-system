using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Tests.Services.Common;

public sealed class TimeZoneServiceTests
{
    [Fact]
    public void CurrentMonthLabel_uses_the_configured_time_zone()
    {
        // 2026-10-31T22:30Z is 2026-11-01 00:30 in Cairo (UTC+2), so the month has already rolled over.
        var service = CreateService("Africa/Cairo", DateTimeOffset.Parse("2026-10-31T22:30:00Z"));

        Assert.Equal("November 2026", service.CurrentMonthLabel());
    }

    [Fact]
    public void MonthLabel_converts_from_utc()
    {
        var service = CreateService("Africa/Cairo", DateTimeOffset.Parse("2026-11-18T09:00:00Z"));

        // 21:00Z on 30 November is 23:00 in Cairo, still November. Two hours later the month has rolled over.
        Assert.Equal("November 2026", service.MonthLabel(DateTimeOffset.Parse("2026-11-30T21:00:00Z")));
        Assert.Equal("December 2026", service.MonthLabel(DateTimeOffset.Parse("2026-11-30T22:30:00Z")));
    }

    [Fact]
    public void Unknown_time_zone_falls_back_to_utc()
    {
        var service = CreateService("Not/A_Zone", DateTimeOffset.Parse("2026-11-18T09:00:00Z"));

        Assert.Equal(TimeZoneInfo.Utc, service.TimeZone);
        Assert.Equal("November 2026", service.CurrentMonthLabel());
    }

    [Fact]
    public void FormatLocal_applies_the_time_zone()
    {
        var service = CreateService("Africa/Cairo", DateTimeOffset.Parse("2026-11-18T09:00:00Z"));

        var formatted = service.FormatLocal(DateTimeOffset.Parse("2026-11-18T21:15:00Z"), "yyyy-MM-dd HH:mm");

        Assert.Equal("2026-11-18 23:15", formatted);
    }

    private static TimeZoneService CreateService(string timeZone, DateTimeOffset now)
    {
        var options = Options.Create(new AppOptions { TimeZone = timeZone });

        return new TimeZoneService(
            new FixedTimeProvider(now),
            options,
            NullLogger<TimeZoneService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
