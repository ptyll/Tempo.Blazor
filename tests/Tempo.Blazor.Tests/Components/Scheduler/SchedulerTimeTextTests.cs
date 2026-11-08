using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The two readings of an event time range. A narrow column gets the compact reading, which prints a
/// shared meridiem once; a culture that has no meridiem, or a range that crosses noon, keeps the
/// full reading, so neither reading loses information.
/// </summary>
public class SchedulerTimeTextTests
{
    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    /// <summary>A non-breaking space — the one that keeps "10:00 AM" in one piece.</summary>
    private const char Nbsp = '\u00A0';

    [Theory]
    [InlineData("en-US", "9:00\u00A0AM – 10:00\u00A0AM", "9:00 – 10:00\u00A0AM")]
    [InlineData("en-GB", "09:00 – 10:00", "09:00 – 10:00")]
    public void Range_Compacts_A_Shared_Meridiem(string culture, string full, string compact)
    {
        var range = SchedulerTimeText.Range(
            new DateTime(2026, 6, 10, 9, 0, 0), new DateTime(2026, 6, 10, 10, 0, 0), Culture(culture));

        range.Full.Should().Be(full);
        range.Compact.Should().Be(compact);
    }

    [Fact]
    public void Range_Keeps_Both_Meridiems_Across_Noon()
    {
        var range = SchedulerTimeText.Range(
            new DateTime(2026, 6, 10, 11, 30, 0), new DateTime(2026, 6, 10, 13, 0, 0), Culture("en-US"));

        range.Full.Should().Be($"11:30{Nbsp}AM – 1:00{Nbsp}PM");
        range.Compact.Should().Be(range.Full, "one designator would be lost");
    }

    [Fact]
    public void Range_Never_Breaks_Inside_A_Time_Only_At_The_En_Dash()
    {
        var range = SchedulerTimeText.Range(
            new DateTime(2026, 6, 10, 10, 0, 0), new DateTime(2026, 6, 10, 11, 30, 0), Culture("en-US"));

        // "10:00 AM" is one unbreakable piece: ICU's narrow no-break space is normalised to U+00A0,
        // never to a plain space. The only break opportunities are the spaces around the en dash.
        range.Full.Should().Contain($"10:00{Nbsp}AM");
        range.Full.Should().Contain($"11:30{Nbsp}AM");
        range.Full.Should().NotContain("10:00 AM");
        range.Full.Should().NotContain("11:30 AM");
        range.Full.Should().Contain($"10:00{Nbsp}AM – 11:30{Nbsp}AM", "the plain spaces around the dash are the breaks");
        range.Compact.Should().Be($"10:00 – 11:30{Nbsp}AM");

        // Only the two plain spaces around the en dash exist, so they are the only break points.
        var spaces = range.Full.Select((c, i) => (c, i)).Where(x => x.c == ' ').Select(x => x.i).ToList();
        spaces.Should().HaveCount(2, "only the separator may break");
        (spaces[1] - spaces[0]).Should().Be(2, "the en dash sits between the two plain spaces");
    }

    [Theory]
    [InlineData("cs-CZ", "9:00 – 10:00")]
    [InlineData("de-DE", "09:00 – 10:00")]
    public void Range_Keeps_A_24_Hour_Culture_Whole(string culture, string expected)
    {
        var range = SchedulerTimeText.Range(
            new DateTime(2026, 6, 10, 9, 0, 0), new DateTime(2026, 6, 10, 10, 0, 0), Culture(culture));

        range.Full.Should().Be(expected);
        range.Compact.Should().Be(expected);
    }
}
