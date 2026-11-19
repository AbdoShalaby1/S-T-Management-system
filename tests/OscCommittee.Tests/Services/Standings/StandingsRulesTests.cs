using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Standings;

namespace OscCommittee.Tests.Services.Standings;

public sealed class StandingsRulesTests
{
    [Fact]
    public void TotalsAreTheSumOfTheLedgerAndBonusSharesTheAdjustmentColumn()
    {
        var tally = StandingsRules.Tally(
        [
            new StandingPoint(PointCategory.DsSolve, 10),
            new StandingPoint(PointCategory.DsSolve, 20),
            new StandingPoint(PointCategory.CSharpSubmission, 7),
            new StandingPoint(PointCategory.Bonus, 5),
            new StandingPoint(PointCategory.Adjustment, -3)
        ]);

        Assert.Equal(30, tally.DsPoints);
        Assert.Equal(7, tally.TaskPoints);
        Assert.Equal(2, tally.BonusPoints);
        Assert.Equal(39, tally.Total);
        Assert.Equal(2, tally.DsSolved);
        Assert.Equal(1, tally.TasksSolved);
    }

    [Fact]
    public void ALateSolveStillCountsAsSolvedButAZeroPointTaskDoesNot()
    {
        var tally = StandingsRules.Tally(
        [
            new StandingPoint(PointCategory.DsSolve, 0),
            new StandingPoint(PointCategory.CSharpSubmission, 0),
            new StandingPoint(PointCategory.CSharpSubmission, 4)
        ]);

        Assert.Equal(1, tally.DsSolved);
        Assert.Equal(1, tally.TasksSolved);
        Assert.Equal(4, tally.Total);
    }

    [Theory]
    [InlineData(StandingsTab.All, 125)]
    [InlineData(StandingsTab.Ds, 20)]
    [InlineData(StandingsTab.Tasks, 5)]
    public void CategoryFiltersPickTheRightColumn(StandingsTab tab, int expected)
    {
        var tally = new StandingsTally(
            DsPoints: 20,
            TaskPoints: 5,
            BonusPoints: 100,
            DsSolved: 2,
            TasksSolved: 1);

        Assert.Equal(expected, tally.PointsFor(tab));
    }

    [Fact]
    public void RankUsesScoreOnlyAndSharesRanksOnTies()
    {
        var registered = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var candidates = new[]
        {
            Candidate("Fewest solves", total: 50, dsSolved: 3, registered.AddDays(1)),
            Candidate("Most solves", total: 50, dsSolved: 8, registered.AddDays(5)),
            Candidate("Same solves, later", total: 50, dsSolved: 8, registered.AddDays(9)),
            Candidate("Same solves, earlier", total: 50, dsSolved: 8, registered)
        };

        var ranked = StandingsRules.Rank(candidates, StandingsTab.All);

        // Equal points share a rank; names only keep the order stable.
        Assert.Equal(
            ["Fewest solves", "Most solves", "Same solves, earlier", "Same solves, later"],
            ranked.Select(row => row.DisplayName));
        Assert.Equal([1, 1, 1, 1], ranked.Select(row => row.Rank));
    }

    [Fact]
    public void RankUsesTheSelectedTab()
    {
        var candidates = new[]
        {
            new StandingsCandidate(
                Guid.NewGuid(),
                "DS player",
                new StandingsTally(DsPoints: 90, TaskPoints: 0, BonusPoints: 0, DsSolved: 9, TasksSolved: 0),
                DateTimeOffset.UnixEpoch),
            new StandingsCandidate(
                Guid.NewGuid(),
                "Task player",
                new StandingsTally(DsPoints: 0, TaskPoints: 40, BonusPoints: 70, DsSolved: 0, TasksSolved: 4),
                DateTimeOffset.UnixEpoch)
        };

        var all = StandingsRules.Rank(candidates, StandingsTab.All);
        Assert.Equal("Task player", all[0].DisplayName);
        Assert.Equal(110, all[0].SortPoints);

        var ds = StandingsRules.Rank(candidates, StandingsTab.Ds);
        Assert.Equal("DS player", ds[0].DisplayName);
        Assert.Equal(90, ds[0].SortPoints);
    }

    [Fact]
    public void IdenticalRecordsFallBackToTheUserIdSoOrderStaysStable()
    {
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var registered = DateTimeOffset.UnixEpoch;
        var tally = new StandingsTally(10, 0, 0, 1, 0);

        var ranked = StandingsRules.Rank(
        [
            new StandingsCandidate(secondId, "Same", tally, registered),
            new StandingsCandidate(firstId, "Same", tally, registered)
        ],
        StandingsTab.All);

        Assert.Equal(firstId, ranked[0].UserId);
    }

    [Theory]
    [InlineData(UserStatus.Active, false, true)]
    [InlineData(UserStatus.Deactivated, false, false)]
    [InlineData(UserStatus.Deactivated, true, true)]
    [InlineData(UserStatus.Pending, true, false)]
    [InlineData(UserStatus.Rejected, true, false)]
    public void DeactivatedMembersAreIncludedOnlyWhenTheBoardAsks(
        UserStatus status,
        bool includeDeactivated,
        bool expected)
    {
        Assert.Equal(expected, StandingsRules.IsEligible(status, includeDeactivated));
    }

    [Theory]
    [InlineData(null, StandingsTab.All)]
    [InlineData("", StandingsTab.All)]
    [InlineData("all", StandingsTab.All)]
    [InlineData("ds", StandingsTab.Ds)]
    [InlineData("DS", StandingsTab.Ds)]
    [InlineData("tasks", StandingsTab.Tasks)]
    [InlineData("nonsense", StandingsTab.All)]
    public void TabParsingFallsBackToAll(string? value, StandingsTab expected)
    {
        Assert.Equal(expected, StandingsQuery.ParseTab(value));
    }

    [Theory]
    [InlineData(null, StandingsView.Summary)]
    [InlineData("summary", StandingsView.Summary)]
    [InlineData("detail", StandingsView.Detail)]
    [InlineData("DETAIL", StandingsView.Detail)]
    public void ViewParsingDefaultsToSummary(string? value, StandingsView expected)
    {
        Assert.Equal(expected, StandingsQuery.ParseView(value));
    }

    [Fact]
    public void EmptyTallyKeepsEveryColumnAtZero()
    {
        var tally = StandingsRules.Tally([]);

        Assert.Equal(0, tally.Total);
        Assert.Equal(0, tally.DsSolved);
        Assert.Equal(0, tally.TasksSolved);
        Assert.Equal(0, tally.PointsFor(StandingsTab.All));
    }

    private static StandingsCandidate Candidate(
        string name,
        int total,
        int dsSolved,
        DateTimeOffset registered) =>
        new(
            Guid.NewGuid(),
            name,
            new StandingsTally(
                DsPoints: total,
                TaskPoints: 0,
                BonusPoints: 0,
                DsSolved: dsSolved,
                TasksSolved: 0),
            registered);
}