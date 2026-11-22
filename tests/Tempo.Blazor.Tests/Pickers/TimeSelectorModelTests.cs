using FluentAssertions;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Additive Abstractions models for the time selector (plan 1c95309c phase 2):
/// <see cref="TimePreset"/>, presentation/fit/hour-cycle/commit enums and
/// <see cref="TimeMinuteStep"/> validation (divisors of 60).
/// </summary>
public class TimeSelectorModelTests
{
    [Fact]
    public void TimePreset_StoresNullableLabelAndTime()
    {
        var named = new TimePreset("Morning", new TimeOnly(7, 0));
        named.Label.Should().Be("Morning");
        named.Time.Should().Be(new TimeOnly(7, 0));

        var anonymous = new TimePreset(null, new TimeOnly(13, 30));
        anonymous.Label.Should().BeNull();
        anonymous.Time.Should().Be(new TimeOnly(13, 30));
    }

    [Fact]
    public void Enums_ExposeTheDocumentedMembers()
    {
        Enum.GetValues<TimePickerPresentation>().Should().Equal(
            TimePickerPresentation.Segments, TimePickerPresentation.Popup, TimePickerPresentation.Auto);
        Enum.GetValues<TimeCommitMode>().Should().Equal(
            TimeCommitMode.Auto, TimeCommitMode.Live, TimeCommitMode.OnConfirm);
        Enum.GetValues<TimeHourCycle>().Should().Equal(
            TimeHourCycle.Auto, TimeHourCycle.H23, TimeHourCycle.H12);
        Enum.GetValues<TimeSelectorGrid>().Should().Equal(
            TimeSelectorGrid.Auto, TimeSelectorGrid.None, TimeSelectorGrid.Always);
        Enum.GetValues<TimeSelectorFit>().Should().Equal(
            TimeSelectorFit.Auto, TimeSelectorFit.Square, TimeSelectorFit.Regular);
        Enum.GetValues<TimeSegment>().Should().Equal(
            TimeSegment.Hours, TimeSegment.Minutes, TimeSegment.Seconds, TimeSegment.DayPeriod);
        Enum.GetValues<AmbiguousTimeResolution>().Should().Equal(
            AmbiguousTimeResolution.Earlier, AmbiguousTimeResolution.Later);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(30)]
    public void MinuteStep_AllowedDivisorsOf60_DoNotThrow(int step)
    {
        var act = () => TimeMinuteStep.Validate(step);
        act.Should().NotThrow();
        TimeMinuteStep.IsAllowed(step).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(45)]
    [InlineData(60)]
    public void MinuteStep_OtherValues_ThrowArgumentOutOfRange(int step)
    {
        var act = () => TimeMinuteStep.Validate(step);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("MinuteStep");
        TimeMinuteStep.IsAllowed(step).Should().BeFalse();
    }

    [Fact]
    public void MinuteStep_Validate_UsesTheSuppliedParameterName()
    {
        var act = () => TimeMinuteStep.Validate(7, "EndMinuteStep");
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("EndMinuteStep")
            .Which.Message.Should().StartWith("EndMinuteStep must");
    }
}
