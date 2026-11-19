using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;

namespace OscCommittee.Tests.Services.Common;

/// <summary>
/// Deadline maths (PLAN §18): a wall-clock deadline the board types is stored as a UTC instant,
/// and members see the same wall clock back even across Egypt's DST transitions.
/// </summary>
public sealed class DeadlineMathTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T09:00:00Z");

    [Theory]
    [InlineData(2026, 1, 15, 18, 0, 16, 0)]   // winter, UTC+2
    [InlineData(2026, 7, 15, 18, 0, 15, 0)]   // summer, UTC+3
    public void A_local_deadline_uses_the_offset_in_force_that_day(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int expectedUtcHour,
        int expectedUtcMinute)
    {
        var service = Create();

        var utc = service.FromLocal(new DateTime(year, month, day, hour, minute, 0)).ToUniversalTime();

        Assert.Equal(year, utc.Year);
        Assert.Equal(month, utc.Month);
        Assert.Equal(day, utc.Day);
        Assert.Equal(expectedUtcHour, utc.Hour);
        Assert.Equal(expectedUtcMinute, utc.Minute);
        Assert.Equal(TimeSpan.Zero, utc.Offset);
    }

    [Fact]
    public void A_deadline_round_trips_to_the_wall_clock_that_was_typed()
    {
        var service = Create();
        var typed = new DateTime(2026, 12, 31, 23, 59, 0);

        var utc = service.FromLocal(typed);
        var local = service.ToLocal(utc);

        Assert.Equal(typed, local.DateTime);
    }

    [Fact]
    public void A_time_inside_the_spring_forward_gap_never_lands_before_the_typed_time()
    {
        // Egypt springs forward at 00:00 on the last Friday of April: 00:30 does not exist.
        var service = Create();
        var typed = new DateTime(2026, 4, 24, 0, 30, 0);

        var utc = service.FromLocal(typed);
        var local = service.ToLocal(utc);

        Assert.True(local.DateTime >= typed);
        Assert.Equal(24, local.Day);
    }

    [Fact]
    public void An_ambiguous_fall_back_time_round_trips_to_the_same_wall_clock()
    {
        // Egypt falls back at 24:00 on the last Thursday of October: 23:30 happens twice.
        var service = Create();
        var typed = new DateTime(2026, 10, 29, 23, 30, 0);

        var utc = service.FromLocal(typed);
        var local = service.ToLocal(utc);

        Assert.Equal(typed, local.DateTime);
    }

    [Fact]
    public void Deadline_validation_uses_utc_not_the_machine_clock()
    {
        var service = Create();
        var due = service.FromLocal(new DateTime(2026, 11, 30, 23, 59, 0));

        Assert.True(due > Now);
        Assert.Equal(DateTimeOffset.Parse("2026-11-30T21:59:00Z"), due);
    }

    private static TimeZoneService Create() =>
        new(
            new Tests.Infrastructure.FixedTimeProvider(Now),
            Options.Create(new AppOptions { TimeZone = "Africa/Cairo" }),
            NullLogger<TimeZoneService>.Instance);
}