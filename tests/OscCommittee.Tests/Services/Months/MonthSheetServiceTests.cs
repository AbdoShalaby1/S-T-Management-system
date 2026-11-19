using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Months;
using OscCommittee.Tests.Infrastructure;

namespace OscCommittee.Tests.Services.Months;

public sealed class MonthSheetServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T09:00:00Z");

    [Fact]
    public async Task Lazy_creation_returns_one_sheet_for_the_month()
    {
        using var database = TestDatabase.Create();
        var (service, clock) = Create(database, Now);

        var first = await service.GetOrCreateAsync(2026, 11);
        clock.UtcNow = Now.AddHours(1);
        var second = await service.GetOrCreateAsync(2026, 11);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(MonthStatus.Open, second.Status);
        Assert.Equal(Now, second.OpenedAtUtc);
        Assert.Single(database.Context.MonthSheets);
    }

    [Fact]
    public async Task The_current_month_comes_from_the_app_time_zone()
    {
        using var database = TestDatabase.Create();
        // 22:30Z on 31 October is 00:30 on 1 November in Cairo.
        var (service, _) = Create(database, DateTimeOffset.Parse("2026-10-31T22:30:00Z"));

        var sheet = await service.GetOrCreateCurrentAsync();

        Assert.Equal(2026, sheet.Year);
        Assert.Equal(11, sheet.Month);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task An_impossible_month_is_rejected(int month)
    {
        using var database = TestDatabase.Create();
        var (service, _) = Create(database, Now);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetOrCreateAsync(2026, month));
    }

    [Fact]
    public async Task Closing_twice_is_idempotent()
    {
        using var database = TestDatabase.Create();
        var (service, _) = Create(database, Now);
        var sheet = await service.GetOrCreateAsync(2026, 11);
        var boardId = Guid.NewGuid();

        var first = await service.CloseAsync(sheet.Id, boardId);
        var second = await service.CloseAsync(sheet.Id, boardId);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Contains("already closed", second.Message);

        var stored = await database.Context.MonthSheets.FindAsync(sheet.Id);
        Assert.NotNull(stored);
        Assert.Equal(MonthStatus.Closed, stored.Status);
        Assert.Equal(boardId, stored.ClosedByUserId);
    }

    [Fact]
    public async Task Reopening_restores_the_open_state()
    {
        using var database = TestDatabase.Create();
        var (service, _) = Create(database, Now);
        var sheet = await service.GetOrCreateAsync(2026, 11);
        var boardId = Guid.NewGuid();
        await service.CloseAsync(sheet.Id, boardId);

        var first = await service.OpenAsync(sheet.Id, boardId);
        var second = await service.OpenAsync(sheet.Id, boardId);

        Assert.True(first.Succeeded);
        Assert.Contains("already open", second.Message);

        var stored = await database.Context.MonthSheets.FindAsync(sheet.Id);
        Assert.NotNull(stored);
        Assert.Equal(MonthStatus.Open, stored.Status);
        Assert.Null(stored.ClosedAtUtc);
        Assert.Null(stored.ClosedByUserId);
    }

    [Fact]
    public async Task Missing_months_report_not_found()
    {
        using var database = TestDatabase.Create();
        var (service, _) = Create(database, Now);

        var close = await service.CloseAsync(Guid.NewGuid(), Guid.NewGuid());
        var open = await service.OpenAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("not_found", close.ErrorCode);
        Assert.Equal("not_found", open.ErrorCode);
    }

    [Fact]
    public async Task The_rollover_closes_only_months_before_the_current_one()
    {
        using var database = TestDatabase.Create();
        var (service, _) = Create(database, Now);
        var october = await service.GetOrCreateAsync(2026, 10);
        var november = await service.GetOrCreateAsync(2026, 11);
        var december = await service.GetOrCreateAsync(2026, 12);

        var closed = await service.CloseElapsedAsync();

        Assert.Equal(1, closed);
        Assert.Equal(MonthStatus.Closed, (await Reload(database, october.Id)).Status);
        Assert.Equal(MonthStatus.Open, (await Reload(database, november.Id)).Status);
        Assert.Equal(MonthStatus.Open, (await Reload(database, december.Id)).Status);

        var secondRun = await service.CloseElapsedAsync();
        Assert.Equal(0, secondRun);
    }

    [Fact]
    public async Task The_list_is_newest_month_first()
    {
        using var database = TestDatabase.Create();
        var (service, _) = Create(database, Now);
        await service.GetOrCreateAsync(2026, 9);
        await service.GetOrCreateAsync(2026, 11);
        await service.GetOrCreateAsync(2027, 1);

        var list = await service.GetListAsync();

        Assert.Equal(new[] { 2027, 2026, 2026 }, list.Select(row => row.Year));
        Assert.Equal(new[] { 1, 11, 9 }, list.Select(row => row.Month));
        Assert.Equal("November 2026", list[1].Label);
    }

    private static (MonthSheetService Service, FixedTimeProvider Clock) Create(
        TestDatabase database,
        DateTimeOffset now)
    {
        var clock = new FixedTimeProvider(now);
        var timeZone = new TimeZoneService(
            clock,
            Options.Create(new AppOptions { TimeZone = "Africa/Cairo" }),
            NullLogger<TimeZoneService>.Instance);
        var service = new MonthSheetService(database.Context, timeZone, clock);

        return (service, clock);
    }

    private static async Task<OscCommittee.Web.Domain.Entities.MonthSheet> Reload(
        TestDatabase database,
        Guid id)
    {
        var stored = await database.Context.MonthSheets.FindAsync(id);
        Assert.NotNull(stored);
        return stored;
    }
}