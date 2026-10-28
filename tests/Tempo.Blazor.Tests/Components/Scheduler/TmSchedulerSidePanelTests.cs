using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Phase 8 TDD: the host side panel and event detail render only through the core
/// <c>TmSidePanel</c> (Presentation = Auto), and the resource filter is the core
/// <c>TmMultiSelect</c>. An empty selection means every resource.
/// </summary>
public class TmSchedulerSidePanelTests : LocalizationTestBase
{
    private static readonly DateTime June = new(2025, 6, 15);

    private static readonly TmScheduleResource[] TwoRooms =
    [
        new() { Id = "room-a", Name = "Room A" },
        new() { Id = "room-b", Name = "Room B" },
    ];

    private static TmScheduleEvent Event(string id, string title, string? resourceId, int day = 15)
        => new()
        {
            Id = id,
            Title = title,
            ResourceId = resourceId,
            Start = new DateTime(2025, 6, day, 9, 0, 0),
            End = new DateTime(2025, 6, day, 10, 0, 0),
        };

    [Fact]
    public void SidePanel_Renders_In_TmSidePanel_Docked_On_Desktop()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.SidePanelOpen, true)
            .Add(c => c.SidePanel, (RenderFragment)(b => b.AddMarkupContent(0, "<p class=\"host-panel\">Panel</p>"))));

        var panel = cut.Find("aside.tm-side-panel--docked");
        panel.GetAttribute("data-side").Should().Be("right");
        panel.InnerHtml.Should().Contain("host-panel");
        cut.FindAll(".tm-drawer").Should().BeEmpty("desktop Auto is docked, never a local sheet");
    }

    [Fact]
    public void SidePanel_Sheet_On_Tablet_And_Bottom_On_Mobile()
    {
        var tablet = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Tablet)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.SidePanelOpen, true)
            .Add(c => c.SidePanel, (RenderFragment)(b => b.AddMarkupContent(0, "<p>Panel</p>"))));

        tablet.FindAll("aside.tm-side-panel--docked").Should().BeEmpty();
        tablet.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--right");

        var mobile = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.SidePanelOpen, true)
            .Add(c => c.SidePanel, (RenderFragment)(b => b.AddMarkupContent(0, "<p>Panel</p>"))));

        mobile.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--bottom");
        mobile.Find(".tm-drawer").GetAttribute("data-restore-target").Should().BeNull();
    }

    [Fact]
    public void EventDetailTemplate_Opens_On_Event_Click_With_SelectedEvent()
    {
        TmScheduleEvent? selected = null;
        var standup = Event("standup", "Standup", "room-a");
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, new[] { standup })
            .Add(c => c.SelectedEvent, standup)
            .Add(c => c.SelectedEventChanged, EventCallback.Factory.Create<TmScheduleEvent?>(this, e => selected = e))
            .Add(c => c.EventDetailTemplate, (RenderFragment<TmScheduleEvent>)(evt =>
                b => b.AddMarkupContent(0, $"<p class=\"detail\">{evt.Title}</p>"))));

        cut.FindAll(".detail").Should().BeEmpty("the detail stays closed until an event is selected");
        cut.Find("[data-event-id='standup']").Click();

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("standup");
        cut.Find("aside.tm-side-panel--docked .detail").TextContent.Should().Contain("Standup");
        cut.Find("[data-event-id='standup']").GetAttribute("aria-current").Should().Be("true");
    }

    [Fact]
    public void EventDetailTemplate_Esc_Closes_And_Returns_Focus_To_Event()
    {
        var standup = Event("standup", "Standup", "room-a");
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Tablet)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, new[] { standup })
            .Add(c => c.EventDetailTemplate, (RenderFragment<TmScheduleEvent>)(_ =>
                b => b.AddMarkupContent(0, "<p class=\"detail\">Standup</p>"))));

        cut.Find("[data-event-id='standup']").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-drawer .detail").Should().NotBeNull());

        // The drawer's focus scope IS the drawer root (TmDrawer wraps its panel in TmFocusScope).
        // The event itself is the opener, so the sheet keeps the focus-scope default (null) and the
        // scheduler's own root-scoped focusRovingStop returns focus.
        var scope = cut.Find(".tm-drawer");
        scope.GetAttribute("data-restore-target").Should().BeNull();
        cut.Find("[data-event-id='standup']").HasAttribute("id").Should().BeFalse(
            "an event id is not unique across views, so none is stamped");

        // Escape is handled by the focus scope (the same path the sheet's JS trap calls), not by a
        // DOM key handler. InvokeAsync keeps the close on the renderer's dispatcher.
        var trap = cut.FindComponent<TmFocusScope>();
        trap.InvokeAsync(() => trap.Instance.HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut.FindAll(".tm-drawer").Should().BeEmpty());
        var focus = JSInterop.Invocations
            .Where(i => i.Identifier == "TempoBlazor.Scheduler.focusRovingStop")
            .ToList();
        focus.Should().NotBeEmpty("Escape returns focus to the event that opened the detail");
    }

    [Fact]
    public void No_SidePanel_Renders_Same_Markup_As_Before()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June));

        cut.FindAll(".tm-side-panel, .tm-drawer, .tm-multiselect, [data-tm-group='filter']").Should().BeEmpty();
        cut.Find(".tm-scheduler-body").Should().NotBeNull();
        // The frame exists only to sit a docked panel beside the body. With both slots unset the
        // body is a direct child of the scheduler, which is the markup a host had before the panel.
        cut.FindAll(".tm-scheduler-frame").Should().BeEmpty();
        cut.Find(".tm-scheduler > .tm-scheduler-body").Should().NotBeNull();
    }

    [Fact]
    public void SelectedResourceIds_Filters_Timeline_Rows_TimeGrid_Columns_Month_And_Agenda_Events()
    {
        var events = new[]
        {
            Event("a", "In A", "room-a"),
            Event("b", "In B", "room-b"),
        };

        var month = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, events)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.SelectedResourceIds, new[] { "room-a" }));
        month.FindAll("[data-event-id]").Select(e => e.GetAttribute("data-event-id"))
            .Should().Equal("a");

        var agenda = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, events)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.SelectedResourceIds, new[] { "room-a" }));
        agenda.FindAll(".tm-scheduler-agenda-event").Should().ContainSingle();
        agenda.Markup.Should().Contain("In A").And.NotContain("In B");

        var timeline = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, events)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.SelectedResourceIds, new[] { "room-a" }));
        timeline.FindAll(".tm-scheduler-timeline-row").Select(r => r.GetAttribute("data-resource"))
            .Should().Equal("room-a");

        var week = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, events)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.SelectedResourceIds, new[] { "room-a" }));
        week.FindAll(".tm-scheduler-timegrid-resource-col").Select(c => c.GetAttribute("data-resource"))
            .Distinct().Should().Equal("room-a");
        week.Markup.Should().NotContain("In B");
    }

    [Fact]
    public void ResourceFilter_Renders_Only_When_ShowResourceFilter()
    {
        var hidden = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Resources, TwoRooms));
        hidden.FindAll(".tm-multiselect, [data-tm-group='filter']").Should().BeEmpty();

        var shown = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.ShowResourceFilter, true)
            .Add(c => c.HeaderActions, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Host</span>")))
            .Add(c => c.OnAddClick, EventCallback.Factory.Create(this, () => { })));

        var group = shown.Find("[data-tm-group='filter']");
        group.Should().NotBeNull();
        shown.Find("[data-testid='scheduler-resource-filter']").Should().NotBeNull();
        // The label is the accessible name, never a visible <label>: the trigger reads "All resources".
        shown.FindAll(".tm-scheduler-toolbar label").Should().BeEmpty();
        shown.Find(".tm-multiselect").GetAttribute("aria-label").Should().Be("Resources");
        shown.Find(".tm-multiselect__placeholder").TextContent.Should().Contain("All resources");

        var groups = shown.FindAll("[data-tm-group]").Select(g => g.GetAttribute("data-tm-group")).ToList();
        groups.Should().ContainInOrder("header-actions", "filter", "primary");
        groups.IndexOf("filter").Should().BeLessThan(groups.IndexOf("export"));
    }

    [Fact]
    public void ResourceFilter_Change_Raises_SelectedResourceIdsChanged()
    {
        IReadOnlyList<string>? reported = null;
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.ShowResourceFilter, true)
            .Add(c => c.SelectedResourceIdsChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, ids => reported = ids)));

        cut.Find(".tm-multiselect").Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-multiselect__option").Should().NotBeEmpty());
        cut.FindAll(".tm-multiselect__option").First(o => o.TextContent.Contains("Room A")).Click();

        reported.Should().NotBeNull();
        reported.Should().Equal("room-a");
    }

    [Fact]
    public void ResourceFilter_On_Mobile_Uses_The_F3_Sheet()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.ShowResourceFilter, true));

        cut.Find("[data-testid='scheduler-resource-filter'] .tm-multiselect").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-drawer--bottom").Should().NotBeNull());
    }

    [Fact]
    public void ResourceFilter_On_Mobile_Is_Its_Own_Row_Under_The_Compact_Header()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.ShowResourceFilter, true));

        var toolbar = cut.Find(".tm-scheduler-toolbar--compact");
        // The compact header stays ‹ title › (Today only without an action bar) and the view menu.
        // The filter is a second row, so it never squeezes that header.
        toolbar.QuerySelector("[data-tm-group='navigation'] [data-tm-group='filter']").Should().BeNull();
        var filter = toolbar.QuerySelector("[data-tm-group='filter']");
        filter.Should().NotBeNull();
        filter!.ClassList.Should().Contain("tm-scheduler-toolbar-filter--row");
        filter.QuerySelector("label").Should().BeNull("the compact trigger carries no visible label");
        filter.QuerySelector(".tm-multiselect__placeholder")!.TextContent.Should().Contain("All resources");
    }

    [Fact]
    public void ResourceFilter_Trigger_Names_The_Selection()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.ShowResourceFilter, true)
            .Add(c => c.SelectedResourceIds, new[] { "room-a", "room-b" }));

        cut.Find(".tm-multiselect__count").TextContent.Trim().Should().Be("2 selected");
        cut.FindAll(".tm-scheduler-toolbar label").Should().BeEmpty();
    }

    [Fact]
    public void Desktop_Escape_Closes_The_Docked_Detail_And_Returns_Focus()
    {
        var standup = Event("standup", "Standup", "room-a");
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, new[] { standup })
            .Add(c => c.EventDetailTemplate, (RenderFragment<TmScheduleEvent>)(_ =>
                b => b.AddMarkupContent(0, "<p class=\"detail\">Standup</p>"))));

        cut.Find("[data-event-id='standup']").Click();
        cut.WaitForAssertion(() => cut.Find("aside.tm-side-panel--docked .detail").Should().NotBeNull());

        var scope = cut.FindComponent<TmFocusScope>();
        scope.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut.FindAll("aside.tm-side-panel--docked").Should().BeEmpty());
        JSInterop.Invocations
            .Where(i => i.Identifier == "TempoBlazor.Scheduler.focusRovingStop")
            .Should().NotBeEmpty("Escape returns focus to the event on the docked panel too");
    }

    [Fact]
    public void CloseDetail_Clears_Only_The_Detail_When_SidePanel_Is_Set()
    {
        bool? open = null;
        var standup = Event("standup", "Standup", "room-a");
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.SidePanelOpen, true)
            .Add(c => c.SidePanelOpenChanged, EventCallback.Factory.Create<bool>(this, v => open = v))
            .Add(c => c.Events, new[] { standup })
            .Add(c => c.SidePanel, (RenderFragment)(b => b.AddMarkupContent(0, "<p class=\"host-panel\">Panel</p>")))
            .Add(c => c.EventDetailTemplate, (RenderFragment<TmScheduleEvent>)(_ =>
                b => b.AddMarkupContent(0, "<p class=\"detail\">Standup</p>"))));

        cut.Find("[data-event-id='standup']").Click();
        cut.WaitForAssertion(() => cut.Find("aside .detail").Should().NotBeNull());
        open = null;

        cut.Find("[data-testid='scheduler-close-detail']").Click();

        cut.FindAll("aside .detail").Should().BeEmpty("the button clears the detail");
        cut.Find("aside .host-panel").Should().NotBeNull("the host panel stays");
        open.Should().BeNull("closing the detail does not close a panel the host opened");
    }

    [Fact]
    public void CloseDetail_Closes_The_Panel_When_Only_The_Detail_Template_Is_Set()
    {
        var standup = Event("standup", "Standup", "room-a");
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, new[] { standup })
            .Add(c => c.EventDetailTemplate, (RenderFragment<TmScheduleEvent>)(_ =>
                b => b.AddMarkupContent(0, "<p class=\"detail\">Standup</p>"))));

        cut.Find("[data-event-id='standup']").Click();
        cut.WaitForAssertion(() => cut.Find("aside .detail").Should().NotBeNull());

        cut.Find("[data-testid='scheduler-close-detail']").Click();

        cut.FindAll("aside.tm-side-panel--docked").Should().BeEmpty("with no host panel the detail is the panel");
    }

    [Fact]
    public void Detail_Closes_When_Its_Event_Leaves_The_Visible_Events()
    {
        var standup = Event("standup", "Standup", "room-a");
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.SidePanelOpen, true)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.Events, new[] { standup })
            .Add(c => c.SidePanel, (RenderFragment)(b => b.AddMarkupContent(0, "<p class=\"host-panel\">Panel</p>")))
            .Add(c => c.EventDetailTemplate, (RenderFragment<TmScheduleEvent>)(_ =>
                b => b.AddMarkupContent(0, "<p class=\"detail\">Standup</p>"))));

        cut.Find("[data-event-id='standup']").Click();
        cut.WaitForAssertion(() => cut.Find("aside .detail").Should().NotBeNull());

        cut.Render(p => p.Add(c => c.SelectedResourceIds, new[] { "room-b" }));

        cut.FindAll("aside .detail").Should().BeEmpty("a filtered-out event has no detail");
        cut.Find("aside .host-panel").Should().NotBeNull("the host panel stays open");
    }

    [Fact]
    public void SelectedResourceIds_With_No_Matching_Id_Shows_Every_Event()
    {
        var events = new[] { Event("a", "In A", "room-a"), Event("b", "In B", "room-b") };
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Events, events)
            .Add(c => c.Resources, TwoRooms)
            .Add(c => c.SelectedResourceIds, new[] { "room-missing" }));

        cut.FindAll("[data-event-id]").Select(e => e.GetAttribute("data-event-id"))
            .Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task Day_Mobile_Resets_The_Visible_Resource_When_It_Is_Filtered_Out()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.Resources, TwoRooms));

        var picker = cut.FindComponents<TmDropdown>().Single(d => d.Markup.Contains("scheduler-resources-menu"));
        await cut.InvokeAsync(() => picker.Instance.SelectItemAsync("room-b"));
        cut.Find("[data-testid='scheduler-resources-menu']").TextContent.Should().Contain("Room B");

        cut.Render(p => p.Add(c => c.SelectedResourceIds, new[] { "room-a" }));

        cut.Find("[data-testid='scheduler-resources-menu']").TextContent.Should().Contain("Room A",
            "a filtered-out resource is not a column, so the picker falls back to the first visible one");
    }

    [Fact]
    public void SidePanelTitle_Names_The_Docked_Header()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.SidePanelOpen, true)
            .Add(c => c.SidePanelTitle, "My calendar")
            .Add(c => c.SidePanel, (RenderFragment)(b => b.AddMarkupContent(0, "<p>Panel</p>"))));

        cut.Find("aside .tm-side-panel__title").TextContent.Should().Be("My calendar");
    }

    [Fact]
    public void SidePanel_Defaults_The_Header_To_The_Localized_Title()
    {
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.CurrentDate, June)
            .Add(c => c.SidePanelOpen, true)
            .Add(c => c.SidePanel, (RenderFragment)(b => b.AddMarkupContent(0, "<p>Panel</p>"))));

        cut.Find("aside .tm-side-panel__title").TextContent.Should().Be("Side panel");
    }
}
