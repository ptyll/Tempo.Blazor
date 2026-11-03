using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 7: the arithmetic behind <c>TmUsageMeter</c>. The share rounding is the part
/// that is easy to get subtly wrong (three equal parts must read 33 + 33 + 34, not 99), and the
/// "unknown limit" rule is the part that keeps a host's missing limit from rendering an infinity.
/// </summary>
public sealed class UsageMeterMathTests
{
    [Theory]
    [InlineData(7_500, 10_000, 7_500)]
    [InlineData(12_000, 10_000, 12_000)]
    [InlineData(-5, 10_000, 0)]
    [InlineData(40, 0, 0)]
    [InlineData(40, -1, 0)]
    public void EffectiveUsed_NeverGoesNegativeAndNeedsAKnownLimit(double used, double limit, double expected)
        => UsageMeterMath.EffectiveUsed(used, limit).Should().Be(expected);

    [Theory]
    [InlineData(0, 10_000, 0)]
    [InlineData(2_500, 10_000, 0.25)]
    [InlineData(10_000, 10_000, 1)]
    [InlineData(15_000, 10_000, 1)]
    [InlineData(10, 0, 0)]
    public void Ratio_IsClampedToThePaintableBand(double used, double limit, double expected)
        => UsageMeterMath.Ratio(used, limit).Should().Be(expected);

    [Fact]
    public void Shares_SumToExactlyOneHundred()
    {
        var shares = UsageMeterMath.Shares([3_333, 3_333, 3_334], 10_000);

        shares.Should().Equal(new[] { 33, 33, 34 });
        shares.Sum().Should().Be(100);
    }

    [Fact]
    public void Shares_GiveTheRemainderToTheLargestFraction_NotToTheLastRow()
    {
        // The two 1/6 rows carry .67 of a point, the two 2/6 rows carry .33: the leftover points go to
        // the SMALLER rows, because their fractions are the larger ones.
        var shares = UsageMeterMath.Shares([2, 2, 1, 1], 6);

        shares.Should().Equal(new[] { 33, 33, 17, 17 });
        shares.Sum().Should().Be(100);
    }

    [Fact]
    public void Shares_OfAZeroTotalAreAllZero_InsteadOfDividingByZero()
    {
        UsageMeterMath.Shares([0, 0], 0).Should().Equal(new[] { 0, 0 });
    }

    [Fact]
    public void Shares_TreatANegativeAmountAsZero()
    {
        var shares = UsageMeterMath.Shares([-2_000, 10_000], 10_000);

        shares.Should().Equal(new[] { 0, 100 });
    }

    [Fact]
    public void Shares_OfNothingAreEmpty()
    {
        UsageMeterMath.Shares([], 100).Should().BeEmpty();
    }

    [Fact]
    public void Shares_AreDeterministicForTiedFractions()
    {
        var first = UsageMeterMath.Shares([1, 1, 1], 3);
        var second = UsageMeterMath.Shares([1, 1, 1], 3);

        first.Should().Equal(new[] { 34, 33, 33 }, "the index breaks a tie, so the earlier row wins the leftover point");
        first.Should().Equal(second);
        first.Sum().Should().Be(100);
    }

    [Fact]
    public void Shares_OnlyExplainWhatTheAmountsAddUpTo()
    {
        // Two equal rows against a bigger declared total: they explain two thirds, so they add up to
        // 66 % and the caller reports the rest (the meter's remainder row does exactly that).
        var shares = UsageMeterMath.Shares([1, 1], 3);

        shares.Should().Equal(new[] { 33, 33 });
    }
}
