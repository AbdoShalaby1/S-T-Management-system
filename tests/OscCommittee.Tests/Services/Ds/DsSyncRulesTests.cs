using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Ds;

namespace OscCommittee.Tests.Services.Ds;

public sealed class DsSyncRulesTests
{
    private static readonly TimeZoneInfo PlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("test-plus-two", TimeSpan.FromHours(2), "Test +2", "Test +2");

    [Fact]
    public void SubmissionAtAssignmentCounts()
    {
        var assigned = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(DsSyncRules.IsAfterAssignment(assigned, assigned));
    }

    [Fact]
    public void SubmissionBeforeAssignmentIsIgnored()
    {
        var assigned = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var solved = assigned.AddSeconds(-1);

        Assert.False(DsSyncRules.IsAfterAssignment(assigned, solved));
    }

    [Fact]
    public void SolveAfterDeadlineIsLate()
    {
        var due = new DateTimeOffset(2026, 11, 30, 21, 59, 0, TimeSpan.Zero);

        Assert.True(DsSyncRules.IsLate(due.AddSeconds(1), due));
        Assert.False(DsSyncRules.IsLate(due, due));
    }

    [Theory]
    [InlineData(2026, 11, 30, 23, 30, 2026, 12)] // 01:30 on 1 December in +2
    [InlineData(2026, 11, 1, 0, 0, 2026, 11)]
    public void CompletionMonthUsesTheAppTimeZone(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int expectedYear,
        int expectedMonth)
    {
        var solved = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        var (actualYear, actualMonth) = DsSyncRules.CompletionMonth(solved, PlusTwo);

        Assert.Equal(expectedYear, actualYear);
        Assert.Equal(expectedMonth, actualMonth);
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(19, false)]
    [InlineData(0, false)]
    public void HistoryGapIsFlaggedOnAFullPage(int rows, bool expected)
    {
        Assert.Equal(expected, DsSyncRules.HistoryGapPossible(rows));
    }

    [Fact]
    public void ExternalIdsAreNormalisedPerPlatform()
    {
        Assert.Equal("two-sum", DsSyncRules.NormalizeExternalId(Platform.LeetCode, "  Two-Sum "));
        Assert.Equal("1800:A", DsSyncRules.NormalizeExternalId(Platform.Codeforces, "1800:a"));
        Assert.Equal("1800:B", DsSyncRules.NormalizeExternalId(Platform.Codeforces, " 1800:b "));
    }

    [Fact]
    public void SolveKeysAreStableAcrossPlatforms()
    {
        Assert.NotEqual(
            DsSyncRules.SolveKey(Platform.LeetCode, "1800:a"),
            DsSyncRules.SolveKey(Platform.Codeforces, "1800:a"));

        Assert.Equal(
            DsSyncRules.SolveKey(Platform.Codeforces, "1800:a"),
            DsSyncRules.SolveKey(Platform.Codeforces, "1800:A"));
    }

    [Fact]
    public void SubmissionUrlsPointAtTheSubmissionWhenPossible()
    {
        Assert.Equal(
            "https://leetcode.com/submissions/detail/2045193157/",
            DsSyncRules.SubmissionUrl(Platform.LeetCode, "two-sum", "2045193157"));

        Assert.Equal(
            "https://leetcode.com/problems/two-sum/",
            DsSyncRules.SubmissionUrl(Platform.LeetCode, "Two-Sum", "manual:1"));

        Assert.Equal(
            "https://codeforces.com/contest/1800/submission/397765878",
            DsSyncRules.SubmissionUrl(Platform.Codeforces, "1800:a", "397765878"));

        Assert.Equal(
            "https://codeforces.com/problemset/problem/1800/A",
            DsSyncRules.SubmissionUrl(Platform.Codeforces, "1800:a", "manual:1"));
    }
}