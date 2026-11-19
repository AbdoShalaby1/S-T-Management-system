using Microsoft.Extensions.DependencyInjection;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Ds;
using OscCommittee.Web.Services.Months;
using Platform = OscCommittee.Web.Domain.Enums.Platform;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// Boards publish a sheet and then type its problems in, so a member can accept a solve before
/// the row exists. The assignment cutoff therefore comes from the sheet, never from the moment
/// the problem row was created.
/// </summary>
public sealed class DsProblemAssignmentTests(OscAppFixture fixture) : IClassFixture<OscAppFixture>
{
    private readonly OscAppFactory _app = fixture.App;

    [Fact]
    public async Task A_problem_added_later_is_assigned_when_its_sheet_was_created()
    {
        var board = await _app.CreateUserAsync(
            WebTestExtensions.UniqueEmail("assignment-board"),
            "Assignment Board",
            board: true);

        Guid sheetId;
        DateTimeOffset createdAt;

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var months = scope.ServiceProvider.GetRequiredService<IMonthSheetService>();
        var problems = scope.ServiceProvider.GetRequiredService<IDsProblemService>();

        var month = await months.GetOrCreateCurrentAsync();
        createdAt = DateTimeOffset.UtcNow.AddHours(-3);

        var sheet = new DsSheet
        {
            MonthSheetId = month.Id,
            Title = "Assignment sheet",
            DueAtUtc = DateTimeOffset.UtcNow.AddDays(7),
            IsActive = false,
            CreatedAtUtc = createdAt
        };

        db.DsSheets.Add(sheet);
        await db.SaveChangesAsync();
        sheetId = sheet.Id;

        var created = await problems.CreateAsync(
            new DsProblemRequest(
                sheetId,
                Platform.LeetCode,
                "two-sum",
                "Two Sum",
                Difficulty.Easy,
                PointsOverride: null,
                Rating: null,
                IsPaidOnly: false),
            board.Id);

        Assert.True(created.Succeeded, created.Message);

        var saved = await db.DsProblems.FindAsync(created.Value);
        Assert.NotNull(saved);
        Assert.Equal(createdAt, saved!.AssignedAtUtc);
    }
}