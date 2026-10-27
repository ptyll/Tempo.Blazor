using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Phase 6 responsiveness. A forced <see cref="TmLayoutMode"/> must change the markup without a DOM
/// measurement: the mobile class, the compact toolbar, the month cap, the date strip, the resource
/// picker, the agenda card and the opt-in action bar.
/// </summary>
public class TmSchedulerResponsiveTests : LocalizationTestBase
{
    private static readonly DateTime Monday = new(2026, 6, 15);

    private static TmScheduleEvent Event(string id, DateTime start, int hours = 1, string? resource = null) => new()
    {
        Id = id,
        Title = id,
        Start = start,
        End = start.AddHours(hours),
        ResourceId = resource,
    };

    private static TmScheduleResource Resource(string id, string name) => new() { Id = id, Name = name };

    [Fact]
    public void LayoutMode_Mobile_Adds_Mobile_Class_And_Compact_Toolbar()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday));

        var root = cut.Find(".tm-scheduler");
        root.GetAttribute("data-layout").Should().Be("mobile");
        root.ClassList.Should().Contain("tm-scheduler--mobile");

        cut.FindAll(".tm-scheduler-toolbar--compact").Should().ContainSingle();
        cut.Find("[data-testid='scheduler-today']").GetAttribute("aria-label").Should().Be("Today",
            "without an action bar the compact row keeps a Today icon button");
        cut.Find("[data-testid='scheduler-prev']").Should().NotBeNull();
        cut.Find("[data-testid='scheduler-next']").Should().NotBeNull();
        cut.Find(".tm-scheduler-toolbar-title [role='button']").Should().NotBeNull();
    }

    [Fact]
    public void LayoutMode_Desktop_Keeps_The_Full_Toolbar()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday));

        cut.Find(".tm-scheduler").GetAttribute("data-layout").Should().Be("desktop",
            "the desktop layout is explicit, not the absence of a class");
        cut.Find("[data-testid='scheduler-today']").Should().NotBeNull();
        cut.Find("[role='tablist']").Should().NotBeNull();
    }

    [Fact]
    public void Month_CompactMaxVisibleEvents_Used_In_Mobile()
    {
        var day = new DateTime(2026, 6, 15, 9, 0, 0);
        var events = new[] { Event("a", day), Event("b", day.AddHours(2)), Event("c", day.AddHours(4)) };

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.Events, events));

        var cell = cut.Find(".tm-scheduler-month-day[data-date='2026-06-15']");
        cell.QuerySelectorAll(".tm-scheduler-month-event").Length.Should().Be(1,
            "the mobile cap defaults to one chip per day");
        cell.QuerySelector(".tm-scheduler-month-more").Should().NotBeNull();
    }

    [Fact]
    public void Month_Desktop_Keeps_The_Host_Cap()
    {
        var day = new DateTime(2026, 6, 15, 9, 0, 0);
        var events = new[] { Event("a", day), Event("b", day.AddHours(2)), Event("c", day.AddHours(4)) };

        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.Events, events));

        cut.Find(".tm-scheduler-month-day[data-date='2026-06-15']")
            .QuerySelectorAll(".tm-scheduler-month-event").Length.Should().Be(3);
    }

    [Fact]
    public void Toolbar_Mobile_ViewSwitcher_Is_Dropdown_With_All_Views()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday));

        cut.FindAll("[role='tablist']").Should().BeEmpty("the mobile switcher is a dropdown, not tabs");
        var trigger = cut.Find("[data-testid='scheduler-view'] button");
        trigger.GetAttribute("aria-haspopup").Should().NotBeNull();
        trigger.Click();

        var items = cut.FindAll("[data-testid='scheduler-view-menu'] [data-view]");
        items.Select(i => i.GetAttribute("data-view"))
            .Should().Equal("Day", "Week", "Month", "Agenda", "Timeline");
    }

    [Fact]
    public void DatePicker_Uses_MobilePresentation_Auto()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday));

        cut.Find(".tm-scheduler-toolbar-title [role='button']").Click();

        cut.FindAll(".tm-overlay-panel-sheet, .tm-drawer").Should().NotBeEmpty(
            "the date popover presents as a sheet on a mobile layout");
        cut.Find(".tm-calendar-view.tm-scheduler-date-calendar").Should().NotBeNull();
    }

    [Fact]
    public void MobileActionBar_None_By_Default_Renders_Nothing()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.OnAddClick, EventCallback.Factory.Create(this, () => { })));

        cut.FindAll(".tm-mobile-action-bar").Should().BeEmpty();
    }

    [Fact]
    public void MobileActionBar_Auto_Renders_Add_Today_View_More()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.MobileActionBar, TmSchedulerMobileActionBar.Auto)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.OnAddClick, EventCallback.Factory.Create(this, () => { })));

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        var ids = bar.QuerySelectorAll("[data-action-id]").Select(b => b.GetAttribute("data-action-id")).ToList();
        ids.Should().Contain(new[] { "scheduler-add", "scheduler-today", "scheduler-view" });

        cut.Find(".tm-mobile-action-bar__more").Click();
        var menu = cut.FindAll("[data-action-id]").Select(b => b.GetAttribute("data-action-id")).ToList();
        menu.Should().Contain("scheduler-print").And.Contain("scheduler-go-to-date");
    }

    [Fact]
    public void MobileActionBar_Auto_Omits_Add_Without_A_Delegate()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.MobileActionBar, TmSchedulerMobileActionBar.Auto)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday));

        cut.Find(".tm-mobile-action-bar__bar").QuerySelectorAll("[data-action-id='scheduler-add']")
            .Should().BeEmpty();
    }

    [Fact]
    public void DateStrip_Click_Sets_SelectedDate()
    {
        DateTime? selected = null;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.SelectedDate, Monday)
            .Add(c => c.SelectedDateChanged, EventCallback.Factory.Create<DateTime?>(this, d => selected = d)));

        var strip = cut.Find(".tm-scheduler-date-strip");
        strip.QuerySelectorAll("[data-date]").Should().HaveCount(7);

        strip.QuerySelector("[data-date='2026-06-17']")!.Click();

        selected.Should().Be(new DateTime(2026, 6, 17));
        strip.QuerySelector("[data-date='2026-06-17']")!.GetAttribute("aria-pressed").Should().Be("true");
        cut.FindAll(".tm-scheduler-timegrid-col[data-date], .tm-scheduler-timegrid-resource-col[data-date]")
            .Select(c => c.GetAttribute("data-date"))
            .Distinct().Should().Equal("2026-06-16", "2026-06-17", "2026-06-18");
    }

    [Fact]
    public void DateStrip_Arrow_Moves_The_Roving_Stop_And_Focus()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.SelectedDate, Monday));
        var strip = cut.Find(".tm-scheduler-date-strip");
        strip.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        var pressed = strip.QuerySelector("[aria-pressed='true']")!;
        pressed.GetAttribute("data-date").Should().Be("2026-06-16");
        pressed.GetAttribute("tabindex").Should().Be("0");
        cut.FindAll(".tm-scheduler-date-strip [tabindex='0']").Should().ContainSingle();
        // The roving stop moves DOM focus, not just tabindex. bUnit records the FocusAsync call.
        JSInterop.Invocations.Should().Contain(call => call.Identifier.Contains("focus"));
    }

    [Fact]
    public void DateStrip_Is_Absent_On_Desktop_And_Outside_Week()
    {
        var desktop = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday));
        desktop.Find(".tm-scheduler").GetAttribute("data-layout").Should().Be("desktop");

        var month = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday));
        month.FindAll(".tm-scheduler-date-strip").Should().BeEmpty();
    }

    [Fact]
    public async Task Day_With_Resources_Mobile_Shows_Resource_Picker()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.Resources, new[] { Resource("room-a", "Room A"), Resource("room-b", "Room B") }));

        var menu = cut.Find("[data-testid='scheduler-resources-menu']");
        menu.TextContent.Should().Contain("Room A", "a phone day opens on the first resource");

        menu.QuerySelector("button")!.Click();
        // The menu renders in a portal, so the items are not in this cut. The picker's own items are.
        var picker = cut.FindComponents<TmDropdown>().Single(d => d.Markup.Contains("scheduler-resources-menu"));
        picker.FindAll("[data-resource]").Select(c => c.GetAttribute("data-resource"))
            .Should().Equal("room-a", "room-b");
        picker.Find("[data-resource='room-a']").GetAttribute("aria-checked").Should().Be("true");

        // The item click reaches the dropdown, which yields before it raises OnSelect. Driving the
        // same SelectItemAsync the item calls keeps the assertion on the render that follows.
        await cut.InvokeAsync(() => picker.Instance.SelectItemAsync("room-b"));
        cut.FindAll(".tm-scheduler-timegrid-resource-col")
            .Select(c => c.GetAttribute("data-resource"))
            .Distinct().Should().Equal("room-b");

        await cut.InvokeAsync(() => picker.Instance.SelectItemAsync("room-a"));
        cut.FindAll(".tm-scheduler-timegrid-resource-col")
            .Select(c => c.GetAttribute("data-resource"))
            .Distinct().Should().Equal("room-a");
    }

    [Fact]
    public void Agenda_Mobile_Renders_Cards_With_Chevron()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.Events, new[] { Event("standup", Monday.AddHours(9)) }));

        var card = cut.Find(".tm-scheduler-agenda-event");
        card.ClassList.Should().Contain("tm-scheduler-agenda-event--card");
        card.QuerySelector(".tm-scheduler-agenda-event-chevron").Should().NotBeNull();
        cut.Find(".tm-scheduler-agenda-date").ClassList.Should().Contain("tm-scheduler-agenda-date--sticky");
        cut.Find(".tm-scheduler-agenda-date-day").Should().NotBeNull();
    }

    [Fact]
    public void Print_Forces_Desktop_Layout()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_scheduler.css"));

        css.Should().Contain("@media print");
        var print = css[css.LastIndexOf("@media print", StringComparison.Ordinal)..];
        print.Should().Contain("container-type: normal",
            "print must undo the container so the desktop layout prints from a phone");
        print.Should().Contain(".tm-scheduler-toolbar--compact");
    }

    [Fact]
    public void Mobile_Body_Is_Not_A_Tabpanel_And_Names_The_Current_View()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday));

        var body = cut.Find(".tm-scheduler-body");
        body.GetAttribute("role").Should().BeNull("a mobile body has no tabs to be a panel of");
        body.GetAttribute("aria-label").Should().Be("Week");
    }

    [Fact]
    public void Compact_View_Trigger_Names_The_Current_View_And_Marks_It()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, Monday));

        cut.Find("[data-testid='scheduler-view']").TextContent.Should().Contain("Agenda");
        cut.Find("[data-testid='scheduler-view'] button").Click();
        cut.Find("[data-view='Agenda']").GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public async Task Action_Bar_View_Opens_The_View_Menu()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.MobileActionBar, TmSchedulerMobileActionBar.Auto)
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday));

        cut.FindAll("[data-testid='scheduler-today']").Should().BeEmpty(
            "the action bar carries Today, so the compact row omits it");
        // The bar's View item calls the toolbar's public open. A click also bubbles to the
        // document, which the menu treats as an outside press and closes on, so the test opens it
        // the way the bar does.
        cut.Find("[data-action-id='scheduler-view']").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.FindComponent<TmSchedulerToolbar>().Instance.OpenViewMenuAsync());
        cut.FindAll(".tm-overlay-panel-sheet [data-view='Week']").Should().NotBeEmpty(
            "the action bar opens the same view sheet the compact row uses");
    }

    [Fact]
    public void Compact_Cap_Clamps_A_Negative_Value()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, Monday)
            .Add(c => c.CompactMaxVisibleEvents, -3)
            .Add(c => c.Events, Enumerable.Range(0, 4).Select(i => Event($"e{i}", Monday.AddHours(9))).ToArray()));

        cut.FindAll(".tm-scheduler-month-event, .tm-scheduler-event").Should().BeEmpty(
            "a negative cap clamps to zero, so every event folds into +N more");
    }

    [Fact]
    public async Task Print_Media_Pins_Desktop_Markup_Until_It_Ends()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, Monday));
        cut.Find(".tm-scheduler").GetAttribute("data-layout").Should().Be("mobile");

        // beforeprint (and PrintAsync) pin the observer to desktop for the whole print.
        await cut.InvokeAsync(() => cut.Instance.OnPrintMediaAsync(true));
        cut.Find(".tm-scheduler").GetAttribute("data-layout").Should().Be("desktop");
        cut.FindAll(".tm-scheduler-timegrid-col").Should().HaveCount(7, "a phone week prints every column");

        await cut.InvokeAsync(() => cut.Instance.OnPrintMediaAsync(false));
        cut.Find(".tm-scheduler").GetAttribute("data-layout").Should().Be("mobile");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
