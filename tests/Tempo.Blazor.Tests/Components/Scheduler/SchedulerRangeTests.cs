using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>The one range computation behind loading, navigation and titles.</summary>
public class SchedulerRangeTests
{
    [Fact]
    public void Month_Range_Is_The_Rendered_Grid()
    {
        // June 2026 starts on a Monday, so a Monday-first grid runs 1 June to 12 July.
        var june = SchedulerRange.Visible(TmScheduleViewType.Month, new DateTime(2026, 6, 15), Options(1));
        june.Start.Should().Be(new DateTime(2026, 6, 1));
        june.End.Should().Be(new DateTime(2026, 7, 13));

        // February 2026 starts on a Sunday, so a Sunday-first grid runs 1 February to 14 March.
        var february = SchedulerRange.Visible(TmScheduleViewType.Month, new DateTime(2026, 2, 10), Options(0));
        february.Start.Should().Be(new DateTime(2026, 2, 1));
        february.End.Should().Be(new DateTime(2026, 3, 15));
    }

    [Fact]
    public void Week_Range_Starts_On_The_Configured_Day()
    {
        var range = SchedulerRange.Visible(TmScheduleViewType.Week, new DateTime(2025, 6, 15), Options(0));
        range.Start.Should().Be(new DateTime(2025, 6, 15));
        range.End.Should().Be(new DateTime(2025, 6, 22));
    }

    [Theory]
    [InlineData(-1, 6)]
    [InlineData(0, 0)]
    [InlineData(8, 1)]
    public void NormalizeFirstDay_Wraps_Into_The_Week(int input, int expected)
        => SchedulerRange.NormalizeFirstDay(input).Should().Be(expected);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(30, 30)]
    public void NormalizeAgendaDays_Is_At_Least_One(int input, int expected)
        => SchedulerRange.NormalizeAgendaDays(input).Should().Be(expected);

    [Fact]
    public void Step_Follows_The_View()
    {
        var day = new DateTime(2025, 6, 15);
        SchedulerRange.Step(TmScheduleViewType.Timeline, day, -1, Options(1)).Should().Be(new DateTime(2025, 6, 14));
        SchedulerRange.Step(TmScheduleViewType.Week, day, 1, Options(1)).Should().Be(new DateTime(2025, 6, 22));
        SchedulerRange.Step(TmScheduleViewType.Agenda, day, 1, new SchedulerRangeOptions(1, 0)).Should().Be(new DateTime(2025, 6, 16),
            "a non-positive agenda length steps by the normalized one day");
    }

    [Theory]
    [InlineData("en-US", "June 15–21, 2026")]
    [InlineData("cs-CZ", "15.–21. června 2026")]
    [InlineData("sk-SK", "15.–21. júna 2026")]
    [InlineData("fr-FR", "15–21 juin 2026")]
    public void FormatRange_Same_Month(string culture, string expected)
        => SchedulerRange.FormatRange(new DateTime(2026, 6, 15), new DateTime(2026, 6, 21), Culture(culture))
            .Should().Be(expected);

    [Theory]
    [InlineData("en-US", "May 15 – June 13, 2025")]
    [InlineData("cs-CZ", "15. května – 13. června 2025")]
    [InlineData("sk-SK", "15. mája – 13. júna 2025")]
    [InlineData("fr-FR", "15 mai – 13 juin 2025")]
    public void FormatRange_Same_Year(string culture, string expected)
        => SchedulerRange.FormatRange(new DateTime(2025, 5, 15), new DateTime(2025, 6, 13), Culture(culture))
            .Should().Be(expected);

    [Theory]
    [InlineData("en-US", "12/28/2025 – 1/4/2026")]
    [InlineData("cs-CZ", "28.12.2025 – 04.01.2026")]
    [InlineData("sk-SK", "28. 12. 2025 – 4. 1. 2026")]
    [InlineData("fr-FR", "28/12/2025 – 04/01/2026")]
    public void FormatRange_Cross_Year(string culture, string expected)
        => SchedulerRange.FormatRange(new DateTime(2025, 12, 28), new DateTime(2026, 1, 4), Culture(culture))
            .Should().Be(expected);

    [Fact]
    public void FormatRange_Single_Day_Is_A_Day_Title()
        => SchedulerRange.FormatRange(new DateTime(2025, 6, 15), new DateTime(2025, 6, 15), Culture("cs-CZ"))
            .Should().Be("15. června 2025", "a one-day range must not read \"15–15\"");

    [Theory]
    [InlineData("en-US", "Sunday, June 15, 2025")]
    [InlineData("cs-CZ", "Neděle 15. června 2025")]
    [InlineData("sk-SK", "Nedeľa 15. júna 2025")]
    [InlineData("fr-FR", "Dimanche 15 juin 2025")]
    public void FormatDayTitle_Capitalizes_The_Long_Date(string culture, string expected)
        => SchedulerRange.FormatDayTitle(new DateTime(2025, 6, 15), Culture(culture)).Should().Be(expected);

    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    private static SchedulerRangeOptions Options(int firstDay) => new(firstDay, 30);
}
