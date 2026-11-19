using Microsoft.EntityFrameworkCore;
using OscCommittee.Web.Data.Configurations;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Tests.Infrastructure;

namespace OscCommittee.Tests.Data;

/// <summary>
/// The database rules the services rely on (PLAN §11, §14.8). SQLite is the provider the plan
/// asks for; the filtered indexes are written in SQL Server syntax and SQLite accepts them.
/// </summary>
public sealed class ModelConfigurationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T09:00:00Z");

    [Fact]
    public async Task A_month_can_only_exist_once()
    {
        using var database = TestDatabase.Create();

        database.Context.MonthSheets.Add(Sheet(2026, 11));
        await database.Context.SaveChangesAsync();

        using var second = database.NewContext();
        second.MonthSheets.Add(Sheet(2026, 11));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_ds_sheet_per_month_can_be_active()
    {
        using var database = TestDatabase.Create();
        var month = Sheet(2026, 11);
        database.Context.MonthSheets.Add(month);
        await database.Context.SaveChangesAsync();

        database.Context.DsSheets.Add(Sheet(month.Id, "Week 1", isActive: true));
        await database.Context.SaveChangesAsync();

        using var second = database.NewContext();
        second.DsSheets.Add(Sheet(month.Id, "Week 2", isActive: true));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Hidden_ds_sheets_do_not_trip_the_active_index()
    {
        using var database = TestDatabase.Create();
        var month = Sheet(2026, 11);
        database.Context.MonthSheets.Add(month);
        await database.Context.SaveChangesAsync();

        database.Context.DsSheets.Add(Sheet(month.Id, "Week 1", isActive: true));
        database.Context.DsSheets.Add(Sheet(month.Id, "Draft", isActive: false));
        database.Context.DsSheets.Add(Sheet(month.Id, "Another draft", isActive: false));

        await database.Context.SaveChangesAsync();

        Assert.Equal(3, await database.Context.DsSheets.CountAsync());
    }

    [Fact]
    public async Task A_platform_problem_credits_one_member_once()
    {
        using var database = TestDatabase.Create();
        var (member, problem) = await SeedProblemAsync(database);

        database.Context.DsSolves.Add(Solve(member.Id, problem.Id, problem.ExternalId));
        await database.Context.SaveChangesAsync();

        using var second = database.NewContext();
        second.DsSolves.Add(Solve(member.Id, problem.Id, problem.ExternalId));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_point_source_can_only_be_credited_once_but_manual_rows_may_share_nulls()
    {
        using var database = TestDatabase.Create();
        var (member, problem) = await SeedProblemAsync(database);
        var sourceId = Guid.NewGuid();

        database.Context.PointEntries.Add(Entry(member.Id, problem.DsSheet!.MonthSheetId, sourceId));
        await database.Context.SaveChangesAsync();

        using (var duplicate = database.NewContext())
        {
            duplicate.PointEntries.Add(Entry(member.Id, problem.DsSheet.MonthSheetId, sourceId));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        using (var manual = database.NewContext())
        {
            manual.PointEntries.Add(Entry(member.Id, problem.DsSheet.MonthSheetId, null, "Bonus A"));
            manual.PointEntries.Add(Entry(member.Id, problem.DsSheet.MonthSheetId, null, "Bonus B"));
            await manual.SaveChangesAsync();
        }

        Assert.Equal(3, await database.Context.PointEntries.CountAsync());
    }

    [Fact]
    public async Task Attempt_numbers_are_unique_per_member_and_task()
    {
        using var database = TestDatabase.Create();
        var (member, problem) = await SeedProblemAsync(database);
        var task = Task(problem.DsSheet!.MonthSheetId, "Build a console app");
        database.Context.CSharpTasks.Add(task);
        await database.Context.SaveChangesAsync();

        database.Context.CSharpSubmissions.Add(Submission(task.Id, member.Id, 1));
        await database.Context.SaveChangesAsync();

        using var second = database.NewContext();
        second.CSharpSubmissions.Add(Submission(task.Id, member.Id, 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_month_cascades_its_sheets_and_problems()
    {
        using var database = TestDatabase.Create();
        var (_, problem) = await SeedProblemAsync(database);
        var monthId = problem.DsSheet!.MonthSheetId;

        using (var delete = database.NewContext())
        {
            var month = await delete.MonthSheets.SingleAsync(sheet => sheet.Id == monthId);
            delete.MonthSheets.Remove(month);
            await delete.SaveChangesAsync();
        }

        Assert.Empty(await database.Context.MonthSheets.ToListAsync());
        Assert.Empty(await database.Context.DsSheets.ToListAsync());
        Assert.Empty(await database.Context.DsProblems.ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_problem_that_has_credits_is_refused()
    {
        using var database = TestDatabase.Create();
        var (member, problem) = await SeedProblemAsync(database);

        database.Context.DsSolves.Add(Solve(member.Id, problem.Id, problem.ExternalId));
        await database.Context.SaveChangesAsync();

        using var delete = database.NewContext();
        var stored = await delete.DsProblems.SingleAsync(row => row.Id == problem.Id);
        delete.DsProblems.Remove(stored);

        await Assert.ThrowsAsync<DbUpdateException>(() => delete.SaveChangesAsync());
        Assert.Single(await database.Context.DsSolves.ToListAsync());
    }

    [Fact]
    public void The_named_indexes_match_the_services()
    {
        using var database = TestDatabase.Create();

        var indexes = database.Context.Model
            .FindEntityType(typeof(ContentPage))!
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToList();

        Assert.Contains(ContentPageConfiguration.ListIndex, indexes);
    }

    private static async Task<(ApplicationUser Member, DsProblem Problem)> SeedProblemAsync(TestDatabase database)
    {
        var member = new ApplicationUser
        {
            DisplayName = "Member One",
            UserName = "member@test.local",
            NormalizedUserName = "MEMBER@TEST.LOCAL",
            Email = "member@test.local",
            NormalizedEmail = "MEMBER@TEST.LOCAL",
            Status = UserStatus.Active,
            CreatedAtUtc = Now
        };

        var month = Sheet(2026, 11);
        var dsSheet = Sheet(month.Id, "Week 1", isActive: true);
        var problem = new DsProblem
        {
            DsSheetId = dsSheet.Id,
            Platform = Platform.LeetCode,
            ExternalId = "two-sum",
            Title = "Two Sum",
            Url = "https://leetcode.com/problems/two-sum/",
            Difficulty = Difficulty.Easy,
            Points = 5,
            AssignedAtUtc = Now.AddDays(-7)
        };

        database.Context.Users.Add(member);
        database.Context.MonthSheets.Add(month);
        database.Context.DsSheets.Add(dsSheet);
        database.Context.DsProblems.Add(problem);
        await database.Context.SaveChangesAsync();

        return (member, problem);
    }

    private static MonthSheet Sheet(int year, int month) => new()
    {
        Year = year,
        Month = month,
        Status = MonthStatus.Open,
        OpenedAtUtc = Now
    };

    

    private static DsSheet Sheet(Guid monthSheetId, string title, bool isActive) => new()
    {
        MonthSheetId = monthSheetId,
        Title = title,
        DueAtUtc = Now.AddDays(14),
        IsActive = isActive,
        CreatedAtUtc = Now
    };

    private static DsSolve Solve(Guid userId, Guid problemId, string externalId) => new()
    {
        UserId = userId,
        DsProblemId = problemId,
        Platform = Platform.LeetCode,
        ExternalId = externalId,
        PlatformSubmissionId = "1000",
        SolvedAtUtc = Now,
        PlatformUrl = "https://leetcode.com/problems/two-sum/",
        CreatedAtUtc = Now
    };

    private static PointEntry Entry(Guid userId, Guid monthSheetId, Guid? sourceId, string? reason = null) => new()
    {
        UserId = userId,
        MonthSheetId = monthSheetId,
        Category = PointCategory.Bonus,
        SourceId = sourceId,
        BasePoints = 1,
        FinalPoints = 1,
        Reason = reason,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private static CSharpTask Task(Guid monthSheetId, string title) => new()
    {
        MonthSheetId = monthSheetId,
        Title = title,
        DueAtUtc = Now.AddDays(14),
        CreatedAtUtc = Now
    };

    private static CSharpSubmission Submission(Guid taskId, Guid userId, int attemptNo) => new()
    {
        TaskId = taskId,
        UserId = userId,
        AttemptNo = attemptNo,
        StoredFolderPath = $"{userId:N}/{Guid.NewGuid():N}",
        BundlePath = $"{userId:N}/{Guid.NewGuid():N}/bundle.md",
        SubmittedAtUtc = Now
    };
}