using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Pure C# contract of <see cref="TimeOfDayFormat"/> (plan 1c95309c phase 2, ADR-0007).
/// No UI: hour cycle, parse matrix, step/clamp without minute overflow, snap, overnight
/// range and DST gap/ambiguous times.
/// </summary>
public class TimeOfDayFormatTests
{
    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    private static TimeZoneInfo Prague()
    {
        foreach (var id in new[] { "Europe/Prague", "Central Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException("Europe/Prague (or Windows CET) time zone is not available.");
    }

    // ── Hour cycle ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("en-US", TimeHourCycle.H12)]
    [InlineData("en-GB", TimeHourCycle.H23)]
    [InlineData("cs", TimeHourCycle.H23)]
    [InlineData("cs-CZ", TimeHourCycle.H23)]
    [InlineData("sk", TimeHourCycle.H23)]
    [InlineData("fr", TimeHourCycle.H23)]
    [InlineData("fr-FR", TimeHourCycle.H23)]
    [InlineData("fr-CA", TimeHourCycle.H23)]
    public void ResolveHourCycle_Auto_FollowsShortTimePattern(string cultureName, TimeHourCycle expected)
    {
        var culture = Culture(cultureName);
        TimeOfDayFormat.ResolveHourCycle(TimeHourCycle.Auto, culture).Should().Be(expected,
            "{0} ShortTimePattern is '{1}' (quoted literals such as fr-CA \"HH 'h' mm\" are not a 12-hour clock)",
            cultureName, culture.DateTimeFormat.ShortTimePattern);
    }

    [Fact]
    public void ResolveHourCycle_ForcedValue_IgnoresCulture()
    {
        TimeOfDayFormat.ResolveHourCycle(TimeHourCycle.H12, Culture("cs")).Should().Be(TimeHourCycle.H12);
        TimeOfDayFormat.ResolveHourCycle(TimeHourCycle.H23, Culture("en-US")).Should().Be(TimeHourCycle.H23);
    }

    [Fact]
    public void Format_enUS_Writes12HourClock()
    {
        var text = TimeOfDayFormat.Format(new TimeOnly(14, 30), TimeHourCycle.Auto, Culture("en-US"));
        text.Should().Contain("2").And.Contain("30");
        text.Should().MatchRegex("(?i)p");
    }

    [Fact]
    public void Format_cs_Writes24HourClock()
    {
        var text = TimeOfDayFormat.Format(new TimeOnly(14, 30), TimeHourCycle.Auto, Culture("cs-CZ"));
        text.Should().Contain("14").And.Contain("30");
        text.Should().NotMatchRegex("(?i)(am|pm|dop|odp)");
    }

    // ── Parse matrix ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1430", 14, 30, 0)]
    [InlineData("14:30", 14, 30, 0)]
    [InlineData("14.30", 14, 30, 0)]
    [InlineData("14h30", 14, 30, 0)]
    [InlineData("14H30", 14, 30, 0)]
    [InlineData("2:30 PM", 14, 30, 0)]
    [InlineData("2:30 pm", 14, 30, 0)]
    [InlineData("2:30PM", 14, 30, 0)]
    [InlineData("2:30p", 14, 30, 0)]
    [InlineData("2:30P", 14, 30, 0)]
    [InlineData("12:00 AM", 0, 0, 0)]
    [InlineData("12:00AM", 0, 0, 0)]
    [InlineData("12:00 a", 0, 0, 0)]
    [InlineData("12:00 PM", 12, 0, 0)]
    [InlineData("1:00 AM", 1, 0, 0)]
    [InlineData("1:00 PM", 13, 0, 0)]
    [InlineData("00:00", 0, 0, 0)]
    [InlineData("23:59", 23, 59, 0)]
    [InlineData("9:05", 9, 5, 0)]
    [InlineData("930", 9, 30, 0)]
    public void TryParse_AcceptsTheDocumentedMatrix(string input, int hour, int minute, int second)
    {
        TimeOfDayFormat.TryParse(input, Culture("en-US"), showSeconds: false, out var time)
            .Should().BeTrue("'{0}' is a documented parse form", input);
        time.Should().Be(new TimeOnly(hour, minute, second));
    }

    [Theory]
    [InlineData("14:30:15", 14, 30, 15)]
    [InlineData("143015", 14, 30, 15)]
    public void TryParse_AcceptsSecondsWhenAllowed(string input, int hour, int minute, int second)
    {
        TimeOfDayFormat.TryParse(input, Culture("en-GB"), showSeconds: true, out var time)
            .Should().BeTrue();
        time.Should().Be(new TimeOnly(hour, minute, second));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("24:00")]
    [InlineData("24:00:00")]
    [InlineData("2400")]
    [InlineData("25:00")]
    [InlineData("14:60")]
    [InlineData("14:30:60")]
    [InlineData("not-a-time")]
    [InlineData("14:30:15")]
    public void TryParse_RejectsEmptyOutOfRangeAndSecondsWhenNotAllowed(string? input)
    {
        TimeOfDayFormat.TryParse(input, Culture("en-GB"), showSeconds: false, out var time)
            .Should().BeFalse("'{0}' must not parse", input);
        time.Should().Be(default);
    }

    [Fact]
    public void TryParse_UsesThePassedCultureForAmPmDesignators_NotTheThreadCulture()
    {
        using (CultureScope.Use("cs"))
        {
            TimeOfDayFormat.TryParse("2:30 PM", Culture("en-US"), showSeconds: false, out var us)
                .Should().BeTrue("en-US AM/PM must parse even when the thread culture is cs");
            us.Should().Be(new TimeOnly(14, 30));

            TimeOfDayFormat.TryParse("2:30 odp.", Culture("cs"), showSeconds: false, out var cs)
                .Should().BeTrue("cs 'odp.' must parse against the culture parameter, not CurrentCulture");
            cs.Should().Be(new TimeOnly(14, 30));
        }

        using (CultureScope.Use("en-US"))
        {
            TimeOfDayFormat.TryParse("2:30 odp.", Culture("cs"), showSeconds: false, out var csOnEnglishThread)
                .Should().BeTrue("cs 'odp.' must not depend on the thread culture matching cs");
            csOnEnglishThread.Should().Be(new TimeOnly(14, 30));
        }
    }

    // ── Step / wrap / clamp (decision 6: minutes do not carry into hours) ───

    [Fact]
    public void Step_Minutes_WrapInsideTheHour_WithoutCarryingToHours()
    {
        TimeOfDayFormat.Step(new TimeOnly(14, 55), TimeSegment.Minutes, delta: +1, minuteStep: 5,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(14, 0));

        TimeOfDayFormat.Step(new TimeOnly(14, 0), TimeSegment.Minutes, delta: -1, minuteStep: 5,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(14, 55));
    }

    [Fact]
    public void Step_Hours_WrapTheDay_WithoutChangingMinutes()
    {
        TimeOfDayFormat.Step(new TimeOnly(23, 30), TimeSegment.Hours, delta: +1, minuteStep: 1,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(0, 30));

        TimeOfDayFormat.Step(new TimeOnly(0, 30), TimeSegment.Hours, delta: -1, minuteStep: 1,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(23, 30));
    }

    [Fact]
    public void Step_Seconds_WrapInsideTheMinute_WithoutCarryingToMinutes()
    {
        TimeOfDayFormat.Step(new TimeOnly(14, 30, 59), TimeSegment.Seconds, delta: +1, minuteStep: 1,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(14, 30, 0));
    }

    [Fact]
    public void Step_AtMax_DoesNotMove()
    {
        var value = new TimeOnly(17, 0);
        TimeOfDayFormat.Step(value, TimeSegment.Hours, delta: +1, minuteStep: 1,
                min: new TimeOnly(9, 0), max: new TimeOnly(17, 0), overnight: false)
            .Should().Be(value);
    }

    [Fact]
    public void Step_AtMin_DoesNotMove()
    {
        var value = new TimeOnly(9, 0);
        TimeOfDayFormat.Step(value, TimeSegment.Hours, delta: -1, minuteStep: 1,
                min: new TimeOnly(9, 0), max: new TimeOnly(17, 0), overnight: false)
            .Should().Be(value);
    }

    [Fact]
    public void Step_PageSizedDeltas_HoursPlus3_MinutesPlus15()
    {
        TimeOfDayFormat.Step(new TimeOnly(10, 0), TimeSegment.Hours, delta: +3, minuteStep: 1,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(13, 0));

        TimeOfDayFormat.Step(new TimeOnly(10, 0), TimeSegment.Minutes, delta: +1, minuteStep: 15,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(10, 15));
    }

    // ── Snap ────────────────────────────────────────────────────────────────

    [Fact]
    public void Step_OffStepMinutes_SnapInTheDirectionOfTheStep()
    {
        TimeOfDayFormat.Step(new TimeOnly(14, 7), TimeSegment.Minutes, delta: +1, minuteStep: 15,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(14, 15));

        TimeOfDayFormat.Step(new TimeOnly(14, 7), TimeSegment.Minutes, delta: -1, minuteStep: 15,
                min: null, max: null, overnight: false)
            .Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public void SnapToStep_AlreadyOnStep_IsUnchanged()
    {
        TimeOfDayFormat.SnapToStep(new TimeOnly(14, 15), minuteStep: 15, direction: +1)
            .Should().Be(new TimeOnly(14, 15));
    }

    [Fact]
    public void RoundUpToStep_Now_RoundsAwayFromZeroUpToTheNextMultiple()
    {
        TimeOfDayFormat.RoundUpToStep(new TimeOnly(14, 7), minuteStep: 15)
            .Should().Be(new TimeOnly(14, 15), "Now rounds up, and may change the hour");

        TimeOfDayFormat.RoundUpToStep(new TimeOnly(14, 0), minuteStep: 15)
            .Should().Be(new TimeOnly(14, 0));

        TimeOfDayFormat.RoundUpToStep(new TimeOnly(14, 46), minuteStep: 15)
            .Should().Be(new TimeOnly(15, 0));

        TimeOfDayFormat.RoundUpToStep(new TimeOnly(23, 59), minuteStep: 15)
            .Should().Be(new TimeOnly(0, 0), "wrapping past midnight is a Now concern, not a segment step");
    }

    [Fact]
    public void RoundUpToStep_MinuteStep1_RoundsSecondsUpAndWrapsMidnight()
    {
        TimeOfDayFormat.RoundUpToStep(new TimeOnly(14, 30, 20), minuteStep: 1)
            .Should().Be(new TimeOnly(14, 31), "step 1 still rounds up; 14:30:20 is not already on the step");

        TimeOfDayFormat.RoundUpToStep(new TimeOnly(23, 59, 30), minuteStep: 1)
            .Should().Be(new TimeOnly(0, 0), "rounding 23:59:30 up by one minute wraps past midnight");
    }

    // ── Overnight range ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(22, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(6, 0, true)]
    [InlineData(6, 1, false)]
    [InlineData(21, 59, false)]
    [InlineData(12, 0, false)]
    public void IsInRange_Overnight_ReadsAsUnionAcrossMidnight(int hour, int minute, bool expected)
    {
        TimeOfDayFormat.IsInRange(
                new TimeOnly(hour, minute),
                min: new TimeOnly(22, 0),
                max: new TimeOnly(6, 0),
                overnight: true)
            .Should().Be(expected);
    }

    [Fact]
    public void IsInRange_OvernightDisabled_WithMinAfterMax_RejectsEverything()
    {
        TimeOfDayFormat.IsInRange(new TimeOnly(23, 0), new TimeOnly(22, 0), new TimeOnly(6, 0), overnight: false)
            .Should().BeFalse();
        TimeOfDayFormat.IsInRange(new TimeOnly(3, 0), new TimeOnly(22, 0), new TimeOnly(6, 0), overnight: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsInRange_InclusiveMinMax_SameDay()
    {
        TimeOfDayFormat.IsInRange(new TimeOnly(9, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), overnight: false)
            .Should().BeTrue();
        TimeOfDayFormat.IsInRange(new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), overnight: false)
            .Should().BeTrue();
        TimeOfDayFormat.IsInRange(new TimeOnly(8, 59), new TimeOnly(9, 0), new TimeOnly(17, 0), overnight: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Step_Overnight_AtDuskMin_DoesNotMoveEarlier()
    {
        var value = new TimeOnly(22, 0);
        TimeOfDayFormat.Step(value, TimeSegment.Hours, delta: -1, minuteStep: 1,
                min: new TimeOnly(22, 0), max: new TimeOnly(6, 0), overnight: true)
            .Should().Be(value);
    }

    [Fact]
    public void Step_Overnight_AtDawnMax_DoesNotMoveLater()
    {
        var value = new TimeOnly(6, 0);
        TimeOfDayFormat.Step(value, TimeSegment.Hours, delta: +1, minuteStep: 1,
                min: new TimeOnly(22, 0), max: new TimeOnly(6, 0), overnight: true)
            .Should().Be(value);
    }

    // ── DST ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetDstGap_EuropePrague_SpringForward_2026_03_29()
    {
        var gap = TimeOfDayFormat.GetDstGap(new DateOnly(2026, 3, 29), Prague());
        gap.Should().NotBeNull();
        gap!.Value.Start.Should().Be(new TimeOnly(2, 0));
        gap.Value.End.Should().Be(new TimeOnly(2, 59));
    }

    [Fact]
    public void IsInvalid_EuropePrague_SpringForward_RejectsTheMissingHour()
    {
        var tz = Prague();
        var date = new DateOnly(2026, 3, 29);
        TimeOfDayFormat.IsInvalid(date, new TimeOnly(2, 0), tz).Should().BeTrue();
        TimeOfDayFormat.IsInvalid(date, new TimeOnly(2, 30), tz).Should().BeTrue();
        TimeOfDayFormat.IsInvalid(date, new TimeOnly(2, 59), tz).Should().BeTrue();
        TimeOfDayFormat.IsInvalid(date, new TimeOnly(1, 59), tz).Should().BeFalse();
        TimeOfDayFormat.IsInvalid(date, new TimeOnly(3, 0), tz).Should().BeFalse();
    }

    [Fact]
    public void IsAmbiguous_EuropePrague_FallBack_2026_10_25()
    {
        var tz = Prague();
        var date = new DateOnly(2026, 10, 25);
        TimeOfDayFormat.IsAmbiguous(date, new TimeOnly(2, 0), tz).Should().BeTrue();
        TimeOfDayFormat.IsAmbiguous(date, new TimeOnly(2, 30), tz).Should().BeTrue();
        TimeOfDayFormat.IsAmbiguous(date, new TimeOnly(2, 59), tz).Should().BeTrue();
        TimeOfDayFormat.IsAmbiguous(date, new TimeOnly(1, 59), tz).Should().BeFalse();
        TimeOfDayFormat.IsAmbiguous(date, new TimeOnly(3, 0), tz).Should().BeFalse();
    }

    [Fact]
    public void GetDstGap_Utc_HasNoGap()
    {
        TimeOfDayFormat.GetDstGap(new DateOnly(2026, 3, 29), TimeZoneInfo.Utc).Should().BeNull();
        TimeOfDayFormat.GetDstGap(new DateOnly(2026, 10, 25), TimeZoneInfo.Utc).Should().BeNull();
    }

    [Fact]
    public void IsAmbiguous_Utc_Never()
    {
        TimeOfDayFormat.IsAmbiguous(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), TimeZoneInfo.Utc)
            .Should().BeFalse();
    }

    [Fact]
    public void GetDstGap_EuropePrague_OnAnOrdinaryDay_IsNull()
    {
        TimeOfDayFormat.GetDstGap(new DateOnly(2026, 6, 15), Prague()).Should().BeNull();
    }
}
