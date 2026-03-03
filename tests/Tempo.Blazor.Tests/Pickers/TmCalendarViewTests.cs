using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>TDD tests for TmCalendarView.</summary>
public class TmCalendarViewTests : LocalizationTestBase
{
    [Fact]
    public void CalendarView_Renders_MonthGrid()
    {
        var cut = RenderComponent<TmCalendarView>();

        cut.Find(".tm-calendar-view").Should().NotBeNull();
        // Month grid should have 42 day cells (6 rows × 7 cols)
        cut.FindAll(".tm-cal-day").Count.Should().Be(42);
    }

    [Fact]
    public void CalendarView_ShowsWeekdayHeaders()
    {
        var cut = RenderComponent<TmCalendarView>();

        cut.FindAll(".tm-cal-header-cell").Count.Should().Be(7);
    }

    [Fact]
    public void CalendarView_ShowsMonthNavigation()
    {
        var cut = RenderComponent<TmCalendarView>();

        cut.Find(".tm-cal-nav").Should().NotBeNull();
        cut.FindAll(".tm-cal-nav-btn").Count.Should().Be(2);
    }

    [Fact]
    public void CalendarView_NavigatePreviousMonth()
    {
        var cut = RenderComponent<TmCalendarView>();

        var title = cut.Find(".tm-cal-title").TextContent;
        cut.Find(".tm-cal-prev").Click();

        var newTitle = cut.Find(".tm-cal-title").TextContent;
        newTitle.Should().NotBe(title);
    }

    [Fact]
    public void CalendarView_NavigateNextMonth()
    {
        var cut = RenderComponent<TmCalendarView>();

        var title = cut.Find(".tm-cal-title").TextContent;
        cut.Find(".tm-cal-next").Click();

        var newTitle = cut.Find(".tm-cal-title").TextContent;
        newTitle.Should().NotBe(title);
    }

    [Fact]
    public void CalendarView_SelectedDate_Highlights()
    {
        var date = new DateOnly(2025, 6, 15);
        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.SelectedDate, date));

        cut.FindAll(".tm-cal-day--selected").Count.Should().Be(1);
    }

    [Fact]
    public void CalendarView_DateClick_FiresCallback()
    {
        DateOnly? clicked = null;
        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.OnDateClick, d => clicked = d));

        // Click a day cell (skip other-month days, find one in current month)
        var days = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)");
        days[0].Click();

        clicked.Should().NotBeNull();
    }

    [Fact]
    public void CalendarView_Events_Render()
    {
        var events = new List<CalendarEvent>
        {
            new() { Date = new DateOnly(2025, 6, 10), Title = "Meeting" },
            new() { Date = new DateOnly(2025, 6, 20), Title = "Deadline" }
        };

        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.SelectedDate, new DateOnly(2025, 6, 1))
            .Add(x => x.Events, events));

        cut.FindAll(".tm-calendar-view__event").Count.Should().Be(2);
    }

    [Fact]
    public void CalendarView_EventClick_FiresCallback()
    {
        CalendarEvent? clicked = null;
        var evt = new CalendarEvent { Date = new DateOnly(2025, 6, 10), Title = "Meeting" };
        var events = new List<CalendarEvent> { evt };

        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.SelectedDate, new DateOnly(2025, 6, 1))
            .Add(x => x.Events, events)
            .Add(x => x.OnEventClick, e => clicked = e));

        cut.Find(".tm-calendar-view__event").Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Meeting");
    }

    [Fact]
    public void CalendarView_HighlightedDates_ShowsIndicator()
    {
        var highlighted = new HashSet<DateOnly>
        {
            new DateOnly(2025, 6, 5),
            new DateOnly(2025, 6, 25)
        };

        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.SelectedDate, new DateOnly(2025, 6, 1))
            .Add(x => x.HighlightedDates, highlighted));

        cut.FindAll(".tm-calendar-view__highlighted").Count.Should().Be(2);
    }

    [Fact]
    public void CalendarView_MinMaxDate_DisablesDays()
    {
        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.SelectedDate, new DateOnly(2025, 6, 15))
            .Add(x => x.MinDate, new DateOnly(2025, 6, 10))
            .Add(x => x.MaxDate, new DateOnly(2025, 6, 20)));

        // Days outside range should be disabled
        cut.FindAll(".tm-cal-day--disabled").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalendarView_TodayHighlight()
    {
        var cut = RenderComponent<TmCalendarView>();

        // Today should be highlighted (assuming we're rendering current month)
        cut.FindAll(".tm-cal-day--today").Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void CalendarView_CustomClass()
    {
        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.Class, "my-calendar"));

        cut.Find(".tm-calendar-view").ClassList.Should().Contain("my-calendar");
    }

    [Fact]
    public void CalendarView_EventWithColor_RendersColor()
    {
        var events = new List<CalendarEvent>
        {
            new() { Date = new DateOnly(2025, 6, 10), Title = "Meeting", Color = "#ef4444" }
        };

        var cut = RenderComponent<TmCalendarView>(p => p
            .Add(x => x.SelectedDate, new DateOnly(2025, 6, 1))
            .Add(x => x.Events, events));

        var eventEl = cut.Find(".tm-calendar-view__event");
        var style = eventEl.GetAttribute("style") ?? "";
        style.Should().Contain("#ef4444");
    }
}
