using OscCommittee.Web.Services.Grading;
using Xunit;

namespace OscCommittee.Tests.Services.Grading;

public sealed class CSharpCreditRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheHighestEffectiveScoreWins()
    {
        var attempts = new[]
        {
            Attempt(1, 2, late: false, hours: 0),
            Attempt(2, 4, late: false, hours: 1)
        };

        Assert.Equal(2, CSharpCreditRules.Select(attempts)!.AttemptNo);
    }

    [Fact]
    public void ALateNineNeverBeatsAnOnTimeSix()
    {
        var attempts = new[]
        {
            Attempt(1, 3, late: false, hours: 0),
            Attempt(2, 4, late: true, hours: 1)
        };

        var best = CSharpCreditRules.Select(attempts);

        Assert.NotNull(best);
        Assert.Equal(1, best!.AttemptNo);
    }

    [Fact]
    public void ABoardEditedLateAttemptDoesAwardPoints()
    {
        var attempts = new[]
        {
            Attempt(1, 3, late: false, hours: 0),
            Attempt(2, 4, late: true, hours: 1, manual: true)
        };

        var best = CSharpCreditRules.Select(attempts);

        Assert.NotNull(best);
        Assert.Equal(2, best!.AttemptNo);
        Assert.Equal(4, CSharpCreditRules.EffectivePoints(best));
    }

    [Fact]
    public void ABoardEditedLateAttemptWithTheSameScoreStillWinsOnTheTieBreak()
    {
        var attempts = new[]
        {
            Attempt(2, 8, late: false, hours: 2),
            Attempt(1, 8, late: true, hours: 1, manual: true)
        };

        Assert.Equal(1, CSharpCreditRules.Select(attempts)!.AttemptNo);
    }

    [Fact]
    public void ATieGoesToTheEarliestSubmission()
    {
        var attempts = new[]
        {
            Attempt(2, 8, late: false, hours: 3),
            Attempt(1, 8, late: false, hours: 1)
        };

        Assert.Equal(1, CSharpCreditRules.Select(attempts)!.AttemptNo);
    }

    [Fact]
    public void ATieAtTheSameInstantGoesToTheLowerAttemptNumber()
    {
        var attempts = new[]
        {
            Attempt(2, 8, late: false, hours: 1),
            Attempt(1, 8, late: false, hours: 1)
        };

        Assert.Equal(1, CSharpCreditRules.Select(attempts)!.AttemptNo);
    }

    [Fact]
    public void OnlyLateAttemptsStillProduceAZeroPointCredit()
    {
        var attempts = new[]
        {
            Attempt(2, 9, late: true, hours: 2),
            Attempt(1, 5, late: true, hours: 1)
        };

        var best = CSharpCreditRules.Select(attempts);

        Assert.NotNull(best);
        Assert.Equal(1, best!.AttemptNo);
        Assert.Equal(0, CSharpCreditRules.EffectivePoints(best));
    }

    [Fact]
    public void UngradedAttemptsAreIgnored()
    {
        var attempts = new[]
        {
            Attempt(1, null, late: false, hours: 0),
            Attempt(2, 7, late: false, hours: 1)
        };

        Assert.Equal(2, CSharpCreditRules.Select(attempts)!.AttemptNo);
    }

    [Fact]
    public void NoGradedAttemptsMeansNothingToCredit()
    {
        Assert.Null(CSharpCreditRules.Select([Attempt(1, null, late: false, hours: 0)]));
        Assert.Null(CSharpCreditRules.Select([]));
    }

    private static CreditableAttempt Attempt(
        int number,
        int? score,
        bool late,
        double hours,
        bool manual = false) =>
        new(
            Guid.NewGuid(),
            number,
            score,
            late,
            manual,
            Start.AddHours(hours));
}