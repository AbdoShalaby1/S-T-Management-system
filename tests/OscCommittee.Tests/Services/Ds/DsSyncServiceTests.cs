using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Data;
using OscCommittee.Web.Domain.Entities;
using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Integrations.Codeforces;
using OscCommittee.Web.Integrations.LeetCode;
using OscCommittee.Web.Integrations;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Ds;
using OscCommittee.Web.Services.Months;
using OscCommittee.Web.Services.Settings;
using OscCommittee.Tests.Infrastructure;
using Platform = OscCommittee.Web.Domain.Enums.Platform;

namespace OscCommittee.Tests.Services.Ds;

/// <summary>
/// The sync engine against captured API shapes (PLAN §18): a new solve, a duplicate, a late solve,
/// an accepted submission older than its assignment, and the twenty-row history-gap flag.
/// </summary>
public sealed class DsSyncServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T09:00:00Z");

    [Fact]
    public async Task A_new_solve_is_credited()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-11-10T12:00:00Z")));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.NewSolves);
        Assert.Equal(5, result.Points);

        var solve = Assert.Single(fixture.Database.Context.DsSolves);
        Assert.Equal("two-sum", solve.ExternalId);
        Assert.False(solve.IsLate);
        Assert.Equal(fixture.Member.Id, solve.UserId);

        var entry = Assert.Single(fixture.Database.Context.PointEntries);
        Assert.Equal(5, entry.FinalPoints);
        Assert.Equal(PointCategory.DsSolve, entry.Category);
        Assert.Equal(fixture.November.Id, entry.MonthSheetId);

        var state = Assert.Single(fixture.Database.Context.SyncStates);
        Assert.False(state.HistoryGapPossible);
        Assert.Equal(1, fixture.LeetCode.Calls);
    }

    [Fact]
    public async Task Back_to_back_syncs_are_not_blocked()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-11-10T12:00:00Z")));
        fixture.LeetCode.Enqueue(Submissions(
            ("101", "two-sum", "2026-11-10T12:05:00Z"),
            ("100", "two-sum", "2026-11-10T12:00:00Z")));

        var first = await fixture.SyncAsync();
        var second = await fixture.SyncAsync();

        Assert.True(first.Succeeded);
        Assert.Equal(1, first.NewSolves);
        Assert.True(second.Succeeded);
        Assert.Null(second.ErrorCode);
        Assert.Equal(0, second.NewSolves);
        Assert.Equal(2, fixture.LeetCode.Calls);
    }

    [Fact]
    public async Task A_solve_the_board_added_later_is_still_credited()
    {
        using var fixture = await Fixture.CreateAsync();

        // The member solved before the board finished typing the sheet in. There is no cursor,
        // so re-reading the window finds it and the sheet-created assignment still allows it.
        fixture.LeetCode.Enqueue(Submissions(
            ("200", "not-on-a-sheet", "2026-11-12T12:00:00Z"),
            ("150", "two-sum", "2026-11-10T12:00:00Z")));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.NewSolves);
        Assert.Single(fixture.Database.Context.DsSolves);
    }

    [Fact]
    public async Task Re_reading_the_window_does_not_recount_old_rows()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-11-10T12:00:00Z")));
        await fixture.SyncAsync();

        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-11-10T12:00:00Z")));
        var second = await fixture.SyncAsync();

        Assert.True(second.Succeeded);
        Assert.Equal(0, second.NewSolves);
        Assert.Empty(second.Problems);
        Assert.Single(fixture.Database.Context.DsSolves);
    }

    [Fact]
    public async Task Submissions_older_than_every_assignment_are_not_walked()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.LeetCode.Enqueue(Submissions(
            ("300", "not-on-a-sheet", "2026-11-15T12:00:00Z"),
            ("200", "two-sum", "2026-10-15T12:00:00Z")));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.NewSolves);
        Assert.Empty(fixture.Database.Context.DsSolves);
    }

    [Fact]
    public async Task Codeforces_finds_a_solve_without_a_cursor()
    {
        using var fixture = await Fixture.CreateAsync(leetCodeHandle: null, codeforcesHandle: "member");

        var assigned = DateTimeOffset.Parse("2026-11-10T12:00:00Z");
        fixture.Codeforces.Enqueue(Result.Ok<IReadOnlyList<CodeforcesSubmission>>(
        [
            Submission(60, "WRONG_ANSWER", Now.AddMinutes(-5), problemType: "PROGRAMMING"),
            Submission(51, "OK", assigned, problemType: "PROGRAMMING")
        ]));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.NewSolves);
        Assert.Equal("51", Assert.Single(fixture.Database.Context.DsSolves).PlatformSubmissionId);
    }

    [Fact]
    public async Task An_already_credited_problem_is_not_saved_again()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Database.Context.DsSolves.Add(new DsSolve
        {
            UserId = fixture.Member.Id,
            DsProblemId = fixture.Problem.Id,
            Platform = Platform.LeetCode,
            ExternalId = "two-sum",
            PlatformSubmissionId = "99",
            SolvedAtUtc = Now.AddDays(-1),
            PlatformUrl = "https://leetcode.com/problems/two-sum/",
            CreatedAtUtc = Now.AddDays(-1)
        });
        await fixture.Database.Context.SaveChangesAsync();
        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-11-10T12:00:00Z")));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.NewSolves);
        Assert.Empty(result.Problems);
        Assert.Single(fixture.Database.Context.DsSolves);
        Assert.Empty(fixture.Database.Context.PointEntries);
    }

    [Fact]
    public async Task A_solve_after_the_deadline_is_recorded_at_zero_in_the_month_it_happened()
    {
        using var fixture = await Fixture.CreateAsync();
        // The sheet is due 23:59 Cairo on 30 November; this accepts at 00:00Z on 1 December.
        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-12-01T00:00:00Z")));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.NewSolves);
        Assert.Equal(0, result.Points);
        Assert.True(Assert.Single(result.Problems).IsLate);

        var solve = Assert.Single(fixture.Database.Context.DsSolves);
        Assert.True(solve.IsLate);

        var entry = Assert.Single(fixture.Database.Context.PointEntries);
        Assert.Equal(0, entry.FinalPoints);

        var december = fixture.Database.Context.MonthSheets
            .Single(sheet => sheet.Year == 2026 && sheet.Month == 12);
        Assert.Equal(december.Id, entry.MonthSheetId);
    }

    [Fact]
    public async Task An_accepted_submission_from_before_the_assignment_is_ignored()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-10-31T23:00:00Z")));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.NewSolves);
        Assert.Empty(result.Problems);
        Assert.Empty(fixture.Database.Context.DsSolves);
        Assert.Empty(fixture.Database.Context.PointEntries);
    }

    [Fact]
    public async Task The_same_problem_on_two_sheets_is_credited_on_both()
    {
        using var fixture = await Fixture.CreateAsync();

        // A board accidentally repeats a problem on another sheet. Both copies are their own
        // assignment, so one accepted submission earns points on each of them.
        var secondSheet = new DsSheet
        {
            MonthSheetId = fixture.November.Id,
            Title = "Week 2",
            DueAtUtc = DateTimeOffset.Parse("2026-11-30T21:59:00Z"),
            IsActive = false,
            CreatedAtUtc = Now
        };

        var secondProblem = new DsProblem
        {
            DsSheetId = secondSheet.Id,
            Platform = Platform.LeetCode,
            ExternalId = "two-sum",
            Title = "Two Sum",
            Url = "https://leetcode.com/problems/two-sum/",
            Difficulty = Difficulty.Easy,
            Points = 5,
            AssignedAtUtc = DateTimeOffset.Parse("2026-11-01T00:00:00Z")
        };

        fixture.Database.Context.DsSheets.Add(secondSheet);
        fixture.Database.Context.DsProblems.Add(secondProblem);
        await fixture.Database.Context.SaveChangesAsync();

        fixture.LeetCode.Enqueue(Submissions(("100", "two-sum", "2026-11-10T12:00:00Z")));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.NewSolves);
        Assert.Equal(10, result.Points);
        Assert.Equal(2, fixture.Database.Context.DsSolves.Count());
        Assert.Equal(2, fixture.Database.Context.PointEntries.Count());
        Assert.Contains(fixture.Database.Context.DsSolves, solve => solve.DsProblemId == fixture.Problem.Id);
        Assert.Contains(fixture.Database.Context.DsSolves, solve => solve.DsProblemId == secondProblem.Id);
    }

    [Fact]
    public async Task A_full_twenty_row_window_flags_a_possible_history_gap()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.LeetCode.Enqueue(Submissions(Enumerable.Range(1, 20)
            .Select(index => ($"{index}", "not-on-a-sheet", "2026-11-10T12:00:00Z"))
            .ToArray()));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.HistoryGapPossible);
        Assert.True(Assert.Single(result.Platforms).HistoryGapPossible);
        Assert.True(Assert.Single(fixture.Database.Context.SyncStates).HistoryGapPossible);
    }

    [Fact]
    public async Task Fewer_than_twenty_rows_do_not_flag_a_gap()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.LeetCode.Enqueue(Submissions(Enumerable.Range(1, 19)
            .Select(index => ($"{index}", "not-on-a-sheet", "2026-11-10T12:00:00Z"))
            .ToArray()));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.HistoryGapPossible);
        Assert.False(Assert.Single(fixture.Database.Context.SyncStates).HistoryGapPossible);
    }

    [Fact]
    public async Task Codeforces_credits_only_accepted_programming_submissions()
    {
        using var fixture = await Fixture.CreateAsync(leetCodeHandle: null, codeforcesHandle: "member");
        var assigned = DateTimeOffset.Parse("2026-11-10T12:00:00Z");
        fixture.Codeforces.Enqueue(Result.Ok<IReadOnlyList<CodeforcesSubmission>>(
        [
            Submission(51, "OK", assigned, problemType: "PROGRAMMING"),
            Submission(50, "WRONG_ANSWER", assigned, problemType: "PROGRAMMING"),
            Submission(49, "OK", assigned, problemType: "QUESTION")
        ]));

        var result = await fixture.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.NewSolves);
        var solve = Assert.Single(fixture.Database.Context.DsSolves);
        Assert.Equal("1:A", solve.ExternalId);
        Assert.Equal("51", solve.PlatformSubmissionId);
        Assert.Equal(Platform.Codeforces, solve.Platform);
    }

    private static CodeforcesSubmission Submission(
        long submissionId,
        string verdict,
        DateTimeOffset submittedAt,
        string? problemType) =>
        new(
            submissionId,
            1,
            "A",
            "Theatre Square",
            verdict,
            submittedAt,
            "C#",
            problemType);

    private static Result<IReadOnlyList<LeetCodeSubmission>> Submissions(
        params (string Id, string Slug, string SubmittedAt)[] rows) =>
        Result.Ok<IReadOnlyList<LeetCodeSubmission>>(
            rows.Select(row => new LeetCodeSubmission(
                row.Id,
                row.Slug,
                row.Slug,
                DateTimeOffset.Parse(row.SubmittedAt),
                "csharp"))
            .ToList());

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            TestDatabase database,
            ApplicationUser member,
            MonthSheet november,
            DsProblem problem,
            FakeLeetCode leetCode,
            FakeCodeforces codeforces,
            DsSyncService service)
        {
            Database = database;
            Member = member;
            November = november;
            Problem = problem;
            LeetCode = leetCode;
            Codeforces = codeforces;
            Service = service;
        }

        public TestDatabase Database { get; }

        public ApplicationUser Member { get; }

        public MonthSheet November { get; }

        public DsProblem Problem { get; }

        public FakeLeetCode LeetCode { get; }

        public FakeCodeforces Codeforces { get; }

        public DsSyncService Service { get; }

        public Task<DsSyncResult> SyncAsync() => Service.SyncAsync(Member.Id);

        public static async Task<Fixture> CreateAsync(
            string? leetCodeHandle = "member",
            string? codeforcesHandle = null)
        {
            var database = TestDatabase.Create();
            var clock = new FixedTimeProvider(Now);
            var member = new ApplicationUser
            {
                DisplayName = "Member One",
                UserName = "member@test.local",
                NormalizedUserName = "MEMBER@TEST.LOCAL",
                Email = "member@test.local",
                NormalizedEmail = "MEMBER@TEST.LOCAL",
                Status = UserStatus.Active,
                LeetCodeHandle = leetCodeHandle,
                CodeforcesHandle = codeforcesHandle,
                CreatedAtUtc = Now
            };

            var november = new MonthSheet
            {
                Year = 2026,
                Month = 11,
                Status = MonthStatus.Open,
                OpenedAtUtc = Now
            };

            var sheet = new DsSheet
            {
                MonthSheetId = november.Id,
                Title = "Week 1",
                DueAtUtc = DateTimeOffset.Parse("2026-11-30T21:59:00Z"),
                IsActive = true,
                CreatedAtUtc = Now
            };

            var problem = codeforcesHandle is not null && leetCodeHandle is null
                ? new DsProblem
                {
                    DsSheetId = sheet.Id,
                    Platform = Platform.Codeforces,
                    ExternalId = "1:A",
                    Title = "Theatre Square",
                    Url = "https://codeforces.com/problemset/problem/1/A",
                    Difficulty = Difficulty.Easy,
                    Points = 5,
                    AssignedAtUtc = DateTimeOffset.Parse("2026-11-01T00:00:00Z")
                }
                : new DsProblem
                {
                    DsSheetId = sheet.Id,
                    Platform = Platform.LeetCode,
                    ExternalId = "two-sum",
                    Title = "Two Sum",
                    Url = "https://leetcode.com/problems/two-sum/",
                    Difficulty = Difficulty.Easy,
                    Points = 5,
                    AssignedAtUtc = DateTimeOffset.Parse("2026-11-01T00:00:00Z")
                };

            database.Context.Users.Add(member);
            database.Context.MonthSheets.Add(november);
            database.Context.DsSheets.Add(sheet);
            database.Context.DsProblems.Add(problem);
            await database.Context.SaveChangesAsync();

            var timeZone = new TimeZoneService(
                clock,
                Options.Create(new AppOptions { TimeZone = "Africa/Cairo" }),
                NullLogger<TimeZoneService>.Instance);
            var months = new MonthSheetService(database.Context, timeZone, clock);
            var credit = new DsCreditService(
                database.Context,
                months,
                timeZone,
                clock,
                NullLogger<DsCreditService>.Instance);
            var leetCode = new FakeLeetCode();
            var codeforces = new FakeCodeforces();
            var service = new DsSyncService(
                database.Context,
                credit,
                leetCode,
                codeforces,
                new DsSyncGate(),
                clock,
                NullLogger<DsSyncService>.Instance);

            return new Fixture(database, member, november, problem, leetCode, codeforces, service);
        }

        public void Dispose() => Database.Dispose();
    }

    private sealed class FakeLeetCode : ILeetCodeClient
    {
        private readonly Queue<Result<IReadOnlyList<LeetCodeSubmission>>> _responses = new();

        public int Calls { get; private set; }

        public void Enqueue(Result<IReadOnlyList<LeetCodeSubmission>> response) =>
            _responses.Enqueue(response);

        public Task<Result<IReadOnlyList<LeetCodeSubmission>>> GetRecentAcceptedAsync(
            string username,
            int limit,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(
                _responses.Count > 0
                    ? _responses.Dequeue()
                    : Result.Ok<IReadOnlyList<LeetCodeSubmission>>([]));
        }

        public Task<Result<LeetCodeProblemMetadata>> GetProblemAsync(
            string slug,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PlatformHandle>> CheckHandleAsync(
            string username,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCodeforces : ICodeforcesClient
    {
        private readonly Queue<Result<IReadOnlyList<CodeforcesSubmission>>> _responses = new();

        public void Enqueue(Result<IReadOnlyList<CodeforcesSubmission>> response) =>
            _responses.Enqueue(response);

        public Task<Result<IReadOnlyList<CodeforcesSubmission>>> GetSubmissionsAsync(
            string handle,
            int count,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _responses.Count > 0
                    ? _responses.Dequeue()
                    : Result.Ok<IReadOnlyList<CodeforcesSubmission>>([]));

        public Task<Result<IReadOnlyList<CodeforcesProblem>>> GetProblemsetAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PlatformHandle>> CheckHandleAsync(
            string handle,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}