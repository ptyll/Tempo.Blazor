using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// The keyboard and naming rules the views share: one activation decision, one grid move, one
/// accessible name. The views call these; they do not re-implement them.
/// </summary>
public class SchedulerA11yTests : LocalizationTestBase
{
    [Fact]
    public void Enter_Activates_Once_And_Space_Only_On_KeyUp()
    {
        SchedulerA11y.IsEnterActivation(Key("Enter")).Should().BeTrue();
        SchedulerA11y.IsEnterActivation(Key("Enter", repeat: true)).Should().BeFalse();
        SchedulerA11y.IsEnterActivation(Key("Enter", ctrl: true)).Should().BeFalse();

        SchedulerA11y.IsSpaceActivation(Key(" ")).Should().BeTrue();
        SchedulerA11y.IsSpaceActivation(Key(" ", shift: true)).Should().BeFalse();
    }

    [Fact]
    public void Modifiers_Are_Never_Roving_Moves()
    {
        SchedulerA11y.HasModifier(Key("ArrowRight", alt: true)).Should().BeTrue();
        SchedulerA11y.HasModifier(Key("ArrowRight", ctrl: true)).Should().BeTrue();
        SchedulerA11y.HasModifier(Key("ArrowRight", meta: true)).Should().BeTrue();
        SchedulerA11y.HasModifier(Key("ArrowRight")).Should().BeFalse();
    }

    [Theory]
    [InlineData("ArrowRight", 0, 7, 42, 1)]
    [InlineData("ArrowLeft", 0, 7, 42, 0)]
    [InlineData("ArrowDown", 0, 7, 42, 7)]
    [InlineData("ArrowUp", 3, 7, 42, 3)]
    [InlineData("Home", 11, 7, 42, 7)]
    [InlineData("End", 8, 7, 42, 13)]
    [InlineData("ArrowRight", 41, 7, 42, 41)]
    [InlineData("PageDown", 5, 7, 42, -1)]
    public void Move_Steps_Inside_The_Grid(string key, int index, int columns, int count, int expected)
    {
        RovingGrid.Move(index, key, columns, count).Should().Be(expected);
    }

    [Fact]
    public void EventLabel_Uses_Words_And_The_Culture_Time()
    {
        var culture = CultureInfo.InvariantCulture;
        var timed = Event("Standup", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 30, 0));

        SchedulerA11y.EventLabel(English(), timed, culture)
            .Should().Be("Standup, 09:00 to 10:30");

        var allDay = Event("Holiday", new DateTime(2025, 6, 10), new DateTime(2025, 6, 11), allDay: true);
        SchedulerA11y.EventLabel(English(), allDay, culture).Should().Be("Holiday, all day");
    }

    [Fact]
    public void EventLabel_Names_Both_Dates_When_The_Event_Spans_Days()
    {
        var culture = CultureInfo.InvariantCulture;
        var spanning = Event("Offsite", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 12, 17, 0, 0));

        var label = SchedulerA11y.EventLabel(English(), spanning, culture);
        label.Should().Contain("10").And.Contain("12").And.Contain(" to ");
    }

    [Fact]
    public void DayCellLabel_Carries_The_Long_Date_And_The_Plural_Count()
    {
        var culture = CultureInfo.InvariantCulture;
        var day = new DateOnly(2025, 6, 10);

        SchedulerA11y.DayCellLabel(English(), day, 0, culture).Should().Contain("10").And.Contain("no events");
        SchedulerA11y.DayCellLabel(English(), day, 1, culture).Should().Contain("1 event");
        SchedulerA11y.DayCellLabel(English(), day, 3, culture).Should().Contain("3 events");
    }

    private static KeyboardEventArgs Key(string key, bool repeat = false, bool alt = false, bool ctrl = false, bool meta = false, bool shift = false)
        => new() { Key = key, Repeat = repeat, AltKey = alt, CtrlKey = ctrl, MetaKey = meta, ShiftKey = shift };

    private static TmScheduleEvent Event(string title, DateTime start, DateTime end, bool allDay = false)
        => new() { Title = title, Start = start, End = end, AllDay = allDay };

    private static ITmLocalizer English() => BuildEnglishLocalizer();
}
