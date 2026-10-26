using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerToolbar titles (B14: the title matches the range that is loaded).</summary>
public class TmSchedulerToolbarTests : LocalizationTestBase
{
    /// <summary>
    /// The toolbar no longer derives the range itself: it titles the range the scheduler hands it.
    /// 15 May to 14 June exclusive is "15. května – 13. června 2025".
    /// </summary>
    [Fact]
    public void Agenda_Title_Shows_Rendered_Range()
    {
        using var _ = CultureScope.Use("cs-CZ");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, new DateTime(2025, 5, 15))
            .Add(c => c.RangeStart, new DateTime(2025, 5, 15))
            .Add(c => c.RangeEnd, new DateTime(2025, 6, 14)));

        cut.Find(".tm-scheduler-toolbar-title [aria-live]").TextContent.Trim()
            .Should().Be("15. května – 13. června 2025");
    }

    /// <summary>
    /// A standalone toolbar that is given no range falls back to a Monday-first week of the current
    /// date, the same default the scheduler uses. 18 June 2025 is a Wednesday, so that week is
    /// [16 June, 23 June).
    /// </summary>
    [Fact]
    public void Standalone_Week_Without_Range_Shows_The_Monday_Week()
    {
        using var _ = CultureScope.Use("en-US");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 18)));

        cut.Find(".tm-scheduler-toolbar-title [aria-live]").TextContent.Trim().Should().Be("June 16–22, 2025");
    }

    /// <summary>Timeline title is the long day title of the range start, not a range.</summary>
    [Fact]
    public void Timeline_Title_Is_Day_Title()
    {
        using var _ = CultureScope.Use("cs-CZ");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.RangeStart, new DateTime(2025, 6, 15))
            .Add(c => c.RangeEnd, new DateTime(2025, 6, 16)));

        cut.Find(".tm-scheduler-toolbar-title [aria-live]").TextContent.Trim()
            .Should().Be("Neděle 15. června 2025");
    }

    /// <summary>
    /// Week title is the genitive range of the week the scheduler computed. A Sunday-first week of
    /// 15 June 2025 is [15 June, 22 June), titled "15.–21. června 2025".
    /// </summary>
    [Fact]
    public void WeekTitle_Shows_The_Given_Range()
    {
        using var _ = CultureScope.Use("cs-CZ");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.RangeStart, new DateTime(2025, 6, 15))
            .Add(c => c.RangeEnd, new DateTime(2025, 6, 22)));

        cut.Find(".tm-scheduler-toolbar-title [aria-live]").TextContent.Trim()
            .Should().Be("15.–21. června 2025");
    }

    /// <summary>
    /// The view switcher changes which panel is shown, so it is a tablist (P17: content switching
    /// belongs to pill tabs, not a segmented radio). The active view is the selected tab and the
    /// only tab stop.
    /// </summary>
    [Fact]
    public void ViewSwitcher_Has_Tablist_And_AriaSelected()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        var group = cut.Find("[role='tablist']");
        group.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.FindAll("[role='radiogroup']").Should().BeEmpty(
            "the switcher is a tablist; a radiogroup would claim a value change");

        var tabs = cut.FindAll("[role='tab']");
        tabs.Count.Should().Be(5);
        tabs.Count(r => r.GetAttribute("aria-selected") == "true").Should().Be(1);
        tabs.Single(r => r.GetAttribute("data-view") == "Week").GetAttribute("aria-selected").Should().Be("true");
        tabs.Count(r => r.GetAttribute("tabindex") == "0").Should().Be(1);
    }

    /// <summary>Previous, Today and Next each have an accessible name.</summary>
    [Fact]
    public void Nav_Buttons_Have_Accessible_Names()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        cut.Find("[data-testid='scheduler-prev']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.Find("[data-testid='scheduler-next']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var today = cut.Find("[data-testid='scheduler-today']");
        var name = today.GetAttribute("aria-label") ?? today.TextContent;
        name.Trim().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>The title announces range changes through a polite live region.</summary>
    [Fact]
    public void Title_Is_AriaLive_Polite()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        var live = cut.Find(".tm-scheduler-toolbar-title .tm-sr-only");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.GetAttribute("aria-atomic").Should().Be("true");
    }

    /// <summary>
    /// Print lives in the "More" menu, never inside the view tablist. The test id is unchanged.
    /// </summary>
    [Fact]
    public void Print_Lives_In_The_More_Menu()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.ShowPrint, true));

        var group = cut.Find("[role='tablist']");
        group.QuerySelector("[data-testid='scheduler-print']").Should().BeNull();
        cut.Find("[data-testid='scheduler-more'] button").Click();
        var print = cut.Find("[data-testid='scheduler-print']");
        print.TextContent.Should().Contain("Print");
        print.GetAttribute("role").Should().Be("menuitem");
    }

    /// <summary>
    /// Desktop order is navigation, title, views, the primary action, then the overflow menu. The
    /// groups are named so a host keys off data-tm-group, never off position.
    /// </summary>
    [Fact]
    public void Groups_Follow_The_Desktop_Order()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.OnAddClick, EventCallback.Factory.Create(this, () => { }))
            .Add(c => c.ShowPrint, true));

        var groups = cut.FindAll("[data-tm-group]")
            .Select(g => g.GetAttribute("data-tm-group"))
            .ToList();
        groups.Should().Equal("navigation", "title", "view", "primary", "overflow");
        cut.Find("[data-tm-group='primary'] [data-testid='scheduler-add']").Should().NotBeNull();
        cut.Find("[data-tm-group='overflow'] [data-testid='scheduler-more']").Should().NotBeNull();
    }

    /// <summary>The add button is opt-in. Without ShowAddButton the primary group is absent.</summary>
    [Fact]
    public void Add_Button_Is_Hidden_By_Default()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        cut.FindAll("[data-testid='scheduler-add']").Should().BeEmpty();
        cut.FindAll("[data-tm-group='primary']").Should().BeEmpty();
    }

    /// <summary>The add button raises OnAddClick and carries the localized accessible name.</summary>
    [Fact]
    public void Add_Button_Raises_OnAddClick()
    {
        var clicked = false;
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.OnAddClick, EventCallback.Factory.Create(this, () => { clicked = true; })));

        var add = cut.Find("[data-testid='scheduler-add']");
        add.TextContent.Should().Contain("New event");
        add.Click();
        clicked.Should().BeTrue();
    }

    /// <summary>
    /// The title is a button that opens a date popover. The heading stays a polite live region,
    /// and the button carries the localized "Go to date" name plus a trailing chevron.
    /// </summary>
    [Fact]
    public void Title_Is_A_GoToDate_Button()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        var title = cut.Find(".tm-scheduler-toolbar-title");
        title.QuerySelector("[aria-live='polite']").Should().NotBeNull();

        var button = title.QuerySelector("[role='button']");
        button.Should().NotBeNull();
        button!.GetAttribute("aria-label").Should().StartWith("červen 2025");
        button.GetAttribute("aria-expanded").Should().Be("false");
        button.InnerHtml.Should().Contain("svg");

        button.Click();
        cut.Find(".tm-scheduler-toolbar-title [role='button']").GetAttribute("aria-expanded").Should().Be("true");
        var calendar = cut.Find(".tm-calendar-view");
        calendar.Closest("[aria-live]").Should().BeNull("the calendar must not sit inside the live region");
    }

    /// <summary>Picking a day in the title popover raises OnDatePicked and closes the popover.</summary>
    [Fact]
    public void Title_Popover_Picks_A_Date()
    {
        DateTime? picked = null;
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.OnDatePicked, EventCallback.Factory.Create<DateTime>(this, d => { picked = d; })));

        cut.Find(".tm-scheduler-toolbar-title [role='button']").Click();
        cut.Find(".tm-calendar-view [data-date='2025-06-20']").Click();

        picked.Should().Be(new DateTime(2025, 6, 20));
        cut.Find(".tm-scheduler-toolbar-title [role='button']").GetAttribute("aria-expanded").Should().Be("false");
    }

    /// <summary>Views keeps the host's order, and a view that is not offered is inserted so one tab stays active.</summary>
    [Fact]
    public void Toolbar_Renders_Only_Configured_Views_In_Given_Order()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Views, new[] { TmScheduleViewType.Month, TmScheduleViewType.Day }));

        cut.FindAll("[role='tab']").Select(t => t.GetAttribute("data-view"))
            .Should().Equal("Month", "Day");
    }

    /// <summary>The active view is inserted at its canonical position when the host did not offer it.</summary>
    [Fact]
    public void View_Not_In_Views_Is_Inserted_At_Its_Canonical_Position()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Views, new[] { TmScheduleViewType.Day, TmScheduleViewType.Month }));

        cut.FindAll("[role='tab']").Select(t => t.GetAttribute("data-view"))
            .Should().Equal("Day", "Week", "Month");
        cut.FindAll("[role='tab']").Count(t => t.GetAttribute("aria-selected") == "true").Should().Be(1);
    }

    /// <summary>The overflow group renders only when it has an entry.</summary>
    [Fact]
    public void More_Absent_Without_Entries()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.ShowPrint, false));

        cut.FindAll("[data-testid='scheduler-more']").Should().BeEmpty();
        cut.FindAll("[data-tm-group='overflow']").Should().BeEmpty();
    }

    /// <summary>Views filters the offered tabs and keeps the active one selected.</summary>
    [Fact]
    public void Views_Filters_The_Switcher()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Views, new[] { TmScheduleViewType.Week, TmScheduleViewType.Month }));

        var tabs = cut.FindAll("[role='tab']");
        tabs.Select(t => t.GetAttribute("data-view")).Should().Equal("Week", "Month");
        tabs.Single(t => t.GetAttribute("aria-selected") == "true").GetAttribute("data-view").Should().Be("Week");
    }

    /// <summary>HeaderStart and HeaderActions render in the named host slots, empty by default.</summary>
    [Fact]
    public void Host_Slots_Render_Where_Named()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.HeaderStart, (RenderFragment)(b => b.AddMarkupContent(0, "<span data-testid='host-start'>S</span>")))
            .Add(c => c.HeaderActions, (RenderFragment)(b => b.AddMarkupContent(0, "<span data-testid='host-actions'>A</span>"))));

        cut.Find("[data-tm-group='header-start'] [data-testid='host-start']").Should().NotBeNull();
        cut.Find("[data-tm-group='header-actions'] [data-testid='host-actions']").Should().NotBeNull();

        var groups = cut.FindAll("[data-tm-group]").Select(g => g.GetAttribute("data-tm-group")).ToList();
        groups.IndexOf("header-start").Should().BeLessThan(groups.IndexOf("navigation"));
        groups.IndexOf("header-actions").Should().BeGreaterThan(groups.IndexOf("view"));
        groups.Should().NotContain("overflow", "print is off, so More has no entries");
    }

    /// <summary>ShowHeader false renders nothing, so a host can supply its own chrome.</summary>
    [Fact]
    public void ShowHeader_False_Renders_Nothing()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.ShowHeader, false));

        cut.FindAll(".tm-scheduler-toolbar").Should().BeEmpty();
    }
}
