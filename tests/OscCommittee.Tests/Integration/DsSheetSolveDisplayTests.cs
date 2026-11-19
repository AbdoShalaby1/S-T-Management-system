using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Months;
using Platform = OscCommittee.Web.Domain.Enums.Platform;

namespace OscCommittee.Tests.Integration;

/// <summary>
/// A platform problem may be assigned on several sheets. Each copy is credited separately, so a
/// member sees the points on every sheet that carries the problem.
/// </summary>
public sealed class DsSheetSolveDisplayTests(OscAppFixture fixture) : IClassFixture<OscAppFixture>
{
    private readonly OscAppFactory _app = fixture.App;

    [Fact]
    public async Task Each_sheet_shows_its_own_credit_for_a_repeated_problem()
    {
        var email = WebTestExtensions.UniqueEmail("duplicate-solve");
        var member = await _app.CreateUserAsync(email, "Duplicate Solver");
        var now = DateTimeOffset.UtcNow;

        Guid earlierSheetId;
        Guid laterSheetId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var months = scope.ServiceProvider.GetRequiredService<IMonthSheetService>();
            var month = await months.GetOrCreateCurrentAsync();

            var earlierSheet = new DsSheet
            {
                MonthSheetId = month.Id,
                Title = "Earlier sheet",
                DueAtUtc = now.AddDays(10),
                IsActive = false,
                CreatedAtUtc = now
            };
            var laterSheet = new DsSheet
            {
                MonthSheetId = month.Id,
                Title = "Later sheet",
                DueAtUtc = now.AddDays(20),
                IsActive = true,
                CreatedAtUtc = now
            };
            var earlierProblem = Problem(earlierSheet.Id, now.AddDays(-10));
            var laterProblem = Problem(laterSheet.Id, now.AddDays(-1));

            var earlierSolve = Solve(member.Id, earlierProblem.Id, "123456", now);
            var laterSolve = Solve(member.Id, laterProblem.Id, "654321", now);

            db.DsSheets.AddRange(earlierSheet, laterSheet);
            db.DsProblems.AddRange(earlierProblem, laterProblem);
            db.DsSolves.AddRange(earlierSolve, laterSolve);
            db.PointEntries.AddRange(
                Entry(member.Id, month.Id, earlierSolve.Id, now),
                Entry(member.Id, month.Id, laterSolve.Id, now));
            await db.SaveChangesAsync();

            earlierSheetId = earlierSheet.Id;
            laterSheetId = laterSheet.Id;
        }

        using var client = await _app.SignedInClientAsync(email, OscAppFactory.MemberPassword);

        using var later = await client.GetAsync($"/ds/{laterSheetId}");
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        var laterHtml = await later.Content.ReadAsStringAsync();
        Assert.Contains("+10 pts", laterHtml);
        Assert.DoesNotContain("credited earlier", laterHtml);
        Assert.DoesNotContain("Not synced", laterHtml);

        using var earlier = await client.GetAsync($"/ds/{earlierSheetId}");
        Assert.Equal(HttpStatusCode.OK, earlier.StatusCode);
        var earlierHtml = await earlier.Content.ReadAsStringAsync();
        Assert.Contains("+10 pts", earlierHtml);
        Assert.DoesNotContain("credited earlier", earlierHtml);
        Assert.DoesNotContain("Not synced", earlierHtml);
    }

    private static DsProblem Problem(Guid sheetId, DateTimeOffset assignedAtUtc) => new()
    {
        DsSheetId = sheetId,
        Platform = Platform.LeetCode,
        ExternalId = "contains-duplicate",
        Title = "Contains Duplicate",
        Url = "https://leetcode.com/problems/contains-duplicate/",
        Difficulty = Difficulty.Easy,
        Points = 10,
        AssignedAtUtc = assignedAtUtc
    };

    private static DsSolve Solve(
        Guid userId,
        Guid problemId,
        string submissionId,
        DateTimeOffset now) => new()
    {
        UserId = userId,
        DsProblemId = problemId,
        Platform = Platform.LeetCode,
        ExternalId = "contains-duplicate",
        PlatformSubmissionId = submissionId,
        SolvedAtUtc = now.AddMinutes(-5),
        Language = "C#",
        PlatformUrl = $"https://leetcode.com/submissions/detail/{submissionId}/",
        CreatedAtUtc = now
    };

    private static PointEntry Entry(
        Guid userId,
        Guid monthSheetId,
        Guid solveId,
        DateTimeOffset now) => new()
    {
        UserId = userId,
        MonthSheetId = monthSheetId,
        Category = PointCategory.DsSolve,
        SourceId = solveId,
        BasePoints = 10,
        FinalPoints = 10,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };
}