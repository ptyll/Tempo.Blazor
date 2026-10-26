using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Review round 1: the work week is the Monday-Friday subset of the configured week, and the
/// working-hour range snaps to the slot grid instead of trusting the raw times.
/// </summary>
public class SchedulerRangeReviewTests
{
    private static readonly DateTime Wednesday = new(2026, 6, 17);

    [Theory]
    [InlineData(1, 15, 19)] // Monday first: the week is already Mon-Fri
    [InlineData(0, 15, 19)] // Sunday first: the work week still starts on Monday
    [InlineData(6, 15, 19)] // Saturday first: the same Monday-Friday
    public void WorkWeek_Is_The_Monday_To_Friday_Subset(int firstDay, int from, int to)
    {
        var dates = SchedulerRange.WeekDates(Wednesday, firstDay, TmSchedulerWeekDays.WorkWeek);

        dates.Should().HaveCount(5);
        dates[0].Day.Should().Be(from);
        dates[^1].Day.Should().Be(to);
        dates.Should().OnlyContain(d => d.DayOfWeek >= DayOfWeek.Monday && d.DayOfWeek <= DayOfWeek.Friday);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public void WorkWeek_Load_Range_Matches_The_Rendered_Days(int firstDay)
    {
        var dates = SchedulerRange.WeekDates(Wednesday, firstDay, TmSchedulerWeekDays.WorkWeek);
        var (start, end) = SchedulerRange.Visible(
            TmScheduleViewType.Week, Wednesday, new SchedulerRangeOptions(firstDay, 30, TmSchedulerWeekDays.WorkWeek));

        start.Should().Be(dates[0].ToDateTime(TimeOnly.MinValue));
        end.Should().Be(dates[^1].ToDateTime(TimeOnly.MinValue).AddDays(1));
    }

    [Fact]
    public void WorkingRange_Floors_The_Start_And_Ceils_The_End_To_The_Slot()
    {
        var (start, end) = SchedulerRange.WorkingRange(new TimeOnly(8, 30), new TimeOnly(17, 15), 60, false);

        start.Should().Be(8 * 60);
        end.Should().Be(18 * 60);
    }

    [Fact]
    public void WorkingRange_Falls_Back_To_The_Full_Day_When_The_End_Is_Not_After_The_Start()
    {
        var (start, end) = SchedulerRange.WorkingRange(new TimeOnly(17, 0), new TimeOnly(8, 0), 60, false);

        start.Should().Be(0);
        end.Should().Be(24 * 60);
    }

    [Fact]
    public void WorkingRange_Is_The_Full_Day_When_ShowFullDay_Is_True()
    {
        var (start, end) = SchedulerRange.WorkingRange(new TimeOnly(8, 0), new TimeOnly(17, 0), 60, true);

        start.Should().Be(0);
        end.Should().Be(24 * 60);
    }
}
