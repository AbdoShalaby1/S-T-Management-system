using OscCommittee.Web.Domain.Enums;
using OscCommittee.Web.Services.Points;
using OscCommittee.Web.Services.Settings;

namespace OscCommittee.Tests.Services.Points;

public sealed class PointsPresetsTests
{
    private static readonly PointsPresets Presets = new(1, 2, 3);

    [Theory]
    [InlineData(Difficulty.Easy, 1)]
    [InlineData(Difficulty.Medium, 2)]
    [InlineData(Difficulty.Hard, 3)]
    public void PresetLookupFollowsDifficulty(Difficulty difficulty, int expected)
    {
        Assert.Equal(expected, Presets.For(difficulty));
    }
}

public sealed class PointEntryRulesTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(-3)]
    [InlineData(999)]
    [InlineData(-999)]
    public void AmountsInsideTheRangeAreAccepted(int amount)
    {
        Assert.Null(PointEntryRules.Validate(amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(-1000)]
    public void ZeroAndOutOfRangeAmountsAreRejected(int amount)
    {
        Assert.NotNull(PointEntryRules.Validate(amount));
    }

    [Fact]
    public void ZeroGetsItsOwnCode()
    {
        Assert.Equal("zero_amount", PointEntryRules.Validate(0)?.Code);
        Assert.Equal("invalid_amount", PointEntryRules.Validate(1000)?.Code);
    }

    [Theory]
    [InlineData(5, "+5")]
    [InlineData(-3, "-3")]
    [InlineData(0, "0")]
    public void AmountsAlwaysCarryASign(int amount, string expected)
    {
        Assert.Equal(expected, PointEntryRules.FormatSigned(amount));
    }
}
