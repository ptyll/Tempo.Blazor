using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for scheduler drag & drop functionality.</summary>
public class TmSchedulerDragTests : LocalizationTestBase
{
    private static TmScheduleEvent Evt(
        string id, string title, DateTime start, DateTime end,
        bool allDay = false) =>
        new() { Id = id, Title = title, Start = start, End = end, AllDay = allDay };

    // ── Month view drag ──

    [Fact]
    public void MonthView_Events_Are_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-month-event");
        eventEl.GetAttribute("draggable").Should().Be("true");
    }

    [Fact]
    public void MonthView_ReadOnly_Events_Not_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true));

        var eventEl = cut.Find(".tm-scheduler-month-event");
        var draggable = eventEl.GetAttribute("draggable");
        (draggable == null || draggable == "false").Should().BeTrue();
    }

    [Fact]
    public void MonthView_DayCells_Accept_Drops()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events));

        // Day cells should have ondrop handler
        var dayCells = cut.FindAll(".tm-scheduler-month-day");
        dayCells.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MonthView_Drop_Fires_OnEventChanged()
    {
        TmScheduleEvent? changed = null;
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.Events, events)
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, e => changed = e)));

        // Simulate dragstart on the event
        var eventEl = cut.Find(".tm-scheduler-month-event");
        eventEl.DragStart();

        // Simulate drop on a different day cell (e.g., June 12)
        var targetDays = cut.FindAll(".tm-scheduler-month-day:not(.tm-scheduler-month-day--other)");
        // Find the day cell for June 12 (index may vary by calendar layout)
        var targetDay = targetDays[11]; // June 12 ≈ index 11 (0-based from June 1)
        targetDay.Drop();

        changed.Should().NotBeNull();
        changed!.Id.Should().Be("e1");
    }

    // ── TimeGrid view drag ──

    [Fact]
    public void TimeGrid_Events_Are_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        eventEl.GetAttribute("draggable").Should().Be("true");
    }

    [Fact]
    public void TimeGrid_ReadOnly_Events_Not_Draggable()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true));

        var eventEl = cut.Find(".tm-scheduler-timegrid-event");
        var draggable = eventEl.GetAttribute("draggable");
        (draggable == null || draggable == "false").Should().BeTrue();
    }

    [Fact]
    public void TimeGrid_Events_Have_Resize_Handle()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events));

        cut.FindAll(".tm-scheduler-event-resize-handle").Count.Should().Be(2);
    }

    [Fact]
    public void TimeGrid_ReadOnly_Events_No_Resize_Handle()
    {
        var events = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2025, 6, 10, 9, 0, 0), new DateTime(2025, 6, 10, 10, 0, 0))
        };

        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2025, 6, 10)])
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-scheduler-event-resize-handle").Count.Should().Be(0);
    }

    // ── Pointer move + keyboard alternative (B13) ──

    private static List<TmScheduleEvent> OneEvent(bool readOnly = false) =>
    [
        new()
        {
            Id = "e1", Title = "Meeting",
            Start = new DateTime(2026, 10, 13, 9, 0, 0),
            End = new DateTime(2026, 10, 13, 10, 0, 0),
            IsReadOnly = readOnly
        }
    ];

    [Fact]
    public void Month_Events_Have_DataEventId_And_Days_Have_DataDate()
    {
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 15))
            .Add(c => c.Events, OneEvent()));

        cut.Find(".tm-scheduler-month-event").GetAttribute("data-event-id").Should().Be("e1");
        cut.Find(".tm-scheduler-month-day").GetAttribute("data-date").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TimeGrid_Columns_Have_DataDate()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14)])
            .Add(c => c.Events, OneEvent()));

        cut.FindAll(".tm-scheduler-timegrid-col").Select(c => c.GetAttribute("data-date"))
            .Should().Equal("2026-10-13", "2026-10-14");
    }

    [Fact]
    public async Task JSInvokable_Move_Complete_Updates_Event_And_Invokes_OnEventChanged()
    {
        var changes = new List<TmScheduleEvent>();
        var events = OneEvent();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14)])
            .Add(c => c.Events, events)
            .Add(c => c.SlotDuration, 30)
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, changes.Add)));

        await cut.InvokeAsync(() => cut.Instance.NotifyMoveComplete("e1", "2026-10-14", 600, null));

        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 14, 10, 0, 0));
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 14, 11, 0, 0), "the duration is kept");
        changes.Should().ContainSingle().Which.Id.Should().Be("e1");
    }

    [Fact]
    public async Task JSInvokable_Resize_Respects_ReadOnly()
    {
        var changes = new List<TmScheduleEvent>();
        var events = OneEvent();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, changes.Add)));

        await cut.InvokeAsync(() => cut.Instance.NotifyResizeComplete("e1", 30, "bottom"));

        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 13, 10, 0, 0));
        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task JSInvokable_Move_Between_Resources_In_Timeline()
    {
        var changes = new List<TmScheduleEvent>();
        var events = OneEvent();
        events[0].ResourceId = "room-a";
        var resources = new List<TmScheduleResource>
        {
            new() { Id = "room-a", Name = "Room A" },
            new() { Id = "room-b", Name = "Room B" }
        };
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, events)
            .Add(c => c.Resources, resources)
            .Add(c => c.OnEventChanged, EventCallback.Factory.Create<TmScheduleEvent>(this, changes.Add)));

        await cut.InvokeAsync(() => cut.Instance.NotifyMoveComplete("e1", "2026-10-13", 600, "room-b"));

        events[0].ResourceId.Should().Be("room-b");
        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 10, 0, 0));
        changes.Should().ContainSingle();
    }

    [Fact]
    public async Task Month_NotifyMoveComplete_KeepsTimeOfDay()
    {
        var events = OneEvent();
        string? announced = null;
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 15))
            .Add(c => c.Events, events)
            .Add(c => c.OnAnnounced, (string text) => announced = text));

        await cut.InvokeAsync(() => cut.Instance.NotifyMoveComplete("e1", "2026-10-20", -1, null));

        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 20, 9, 0, 0), "minuteOfDay -1 keeps the time");
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 20, 10, 0, 0));
        announced.Should().NotBeNull().And.NotContain("9:00", "a month move announces the date, not the time");
    }

    [Fact]
    public async Task NotifyMoveComplete_ReadOnly_Or_EventReadOnly_IsIgnored()
    {
        var gridEvents = OneEvent();
        var grid = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, gridEvents)
            .Add(c => c.ReadOnly, true));
        await grid.InvokeAsync(() => grid.Instance.NotifyMoveComplete("e1", "2026-10-14", 600, null));
        gridEvents[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0));

        var locked = OneEvent(readOnly: true);
        var open = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, locked));
        await open.InvokeAsync(() => open.Instance.NotifyMoveComplete("e1", "2026-10-14", 600, null));
        locked[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0));
    }

    [Fact]
    public async Task Keyboard_AltArrow_Moves_Event_By_Slot()
    {
        var events = OneEvent();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14)])
            .Add(c => c.Events, events)
            .Add(c => c.SlotDuration, 30));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, 1, false));
        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 30, 0));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 1, 0, false));
        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 14, 9, 30, 0), "right moves a day");
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 14, 10, 30, 0));
    }

    [Fact]
    public async Task Keyboard_AltShiftArrow_Resizes_Event()
    {
        var events = OneEvent();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, events)
            .Add(c => c.SlotDuration, 30));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, 1, true));

        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0), "a resize keeps the start");
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 13, 10, 30, 0));
    }

    [Fact]
    public async Task Keyboard_Move_Ignored_When_ReadOnly()
    {
        var events = OneEvent();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, events)
            .Add(c => c.ReadOnly, true));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, 1, false));
        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, 1, true));

        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0));
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 13, 10, 0, 0));
    }

    [Fact]
    public async Task TmScheduler_Renders_PoliteStatusRegion_And_AnnouncesMove()
    {
        var events = OneEvent();
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, events));

        var regions = cut.FindAll("[role='status']");
        regions.Should().ContainSingle("one live region, so a view swap cannot drop an announcement");
        var region = regions.Single();
        region.ClassList.Should().Contain("tm-sr-only");
        region.GetAttribute("aria-live").Should().Be("polite");

        var grid = cut.FindComponent<TmSchedulerTimeGrid>();
        await cut.InvokeAsync(() => grid.Instance.NotifyKeyboardStep("e1", 0, 1, false));

        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 30, 0), "the step has to land before it can be announced");
        cut.Find("[role='status']").TextContent.Should().Contain("9:30");
    }

    [Fact]
    public async Task Keyboard_Move_Across_Column_Focuses_The_Moved_Event()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.SetupVoid("TempoBlazor.Scheduler.registerInstance", _ => true);
        var focus = JSInterop.SetupVoid("TempoBlazor.Scheduler.focusRovingStop", _ => true);

        var events = OneEvent();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14)])
            .Add(c => c.Events, events)
            .Add(c => c.SlotDuration, 30));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 1, 0, false));

        var moved = cut.Find("[data-event-id='e1']");
        moved.GetAttribute("tabindex").Should().Be("0", "the moved event is the new roving stop");
        focus.Invocations.Should().Contain(call =>
            call.Arguments.Count == 2
            && call.Arguments[1] != null
            && call.Arguments[1].ToString() == "[data-event-id=\"e1\"]");
    }

    [Fact]
    public async Task Keyboard_AltShiftUp_Shrinks_End()
    {
        var events = OneEvent();
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, events)
            .Add(c => c.SlotDuration, 30));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, -1, true));

        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0), "only the end moves");
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 13, 9, 30, 0));
    }

    [Fact]
    public async Task Keyboard_AltShiftLeft_Shrinks_Timeline_End_And_Ignores_Vertical()
    {
        var events = OneEvent();
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, events)
            .Add(c => c.SlotDuration, 30));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, -1, true));
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 13, 10, 0, 0), "Up/Down is not a timeline resize");

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", -1, 0, true));
        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0));
        events[0].EndLocal.Should().Be(new DateTime(2026, 10, 13, 9, 30, 0));
    }

    [Fact]
    public async Task Keyboard_Move_Clamps_To_The_Visible_Range()
    {
        var midnight = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2026, 10, 13, 0, 0, 0), new DateTime(2026, 10, 13, 1, 0, 0))
        };
        var up = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, midnight)
            .Add(c => c.SlotDuration, 30));
        await up.InvokeAsync(() => up.Instance.NotifyKeyboardStep("e1", 0, -1, false));
        midnight[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 0, 0, 0), "00:00 cannot step before the range");

        var late = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2026, 10, 13, 23, 30, 0), new DateTime(2026, 10, 14, 0, 30, 0))
        };
        var down = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, late)
            .Add(c => c.SlotDuration, 30));
        await down.InvokeAsync(() => down.Instance.NotifyKeyboardStep("e1", 0, 1, false));
        late[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 23, 30, 0), "the start cannot leave the day");

        var work = new List<TmScheduleEvent>
        {
            Evt("e1", "Meeting", new DateTime(2026, 10, 13, 8, 0, 0), new DateTime(2026, 10, 13, 9, 0, 0))
        };
        var clipped = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, work)
            .Add(c => c.SlotDuration, 30)
            .Add(c => c.ShowFullDay, false));
        await clipped.InvokeAsync(() => clipped.Instance.NotifyKeyboardStep("e1", 0, -1, false));
        work[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 8, 0, 0), "ShowFullDay false starts at 08:00");
    }

    [Fact]
    public async Task Timeline_Without_Resources_Keeps_ResourceId_Null()
    {
        var events = OneEvent();
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, events));

        await cut.InvokeAsync(() => cut.Instance.NotifyMoveComplete("e1", "2026-10-13", 600, "_default"));
        events[0].ResourceId.Should().BeNull();

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 1, 0, false));
        events[0].ResourceId.Should().BeNull();
        events[0].StartLocal.Date.Should().Be(new DateTime(2026, 10, 13), "a timeline step keeps the event's own date");
    }

    [Fact]
    public async Task Move_Across_Dst_Uses_The_Display_Zone_Offset()
    {
        var prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        var start = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.FromHours(2));
        var events = new List<TmScheduleEvent>
        {
            new() { Id = "e1", Title = "Meeting", Start = start, End = start.AddHours(1) }
        };
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25)])
            .Add(c => c.Events, events)
            .Add(c => c.TimeZone, prague));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 1, 0, false));

        events[0].Start.Offset.Should().Be(TimeSpan.FromHours(1), "25 Oct 2026 is +01:00 in Prague");
        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 25, 22, 0, 0));
    }

    [Fact]
    public async Task Month_Keyboard_Up_Moves_A_Week()
    {
        var events = OneEvent();
        var cut = Render<TmSchedulerMonthView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 15))
            .Add(c => c.Events, events));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, -1, false));

        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 6, 9, 0, 0));
    }

    [Fact]
    public async Task Timeline_Keyboard_Up_Moves_To_The_Previous_Resource()
    {
        var events = OneEvent();
        events[0].ResourceId = "room-b";
        var cut = Render<TmSchedulerTimelineView>(p => p
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, events)
            .Add(c => c.Resources, new List<TmScheduleResource>
            {
                new() { Id = "room-a", Name = "Room A" },
                new() { Id = "room-b", Name = "Room B" }
            }));

        await cut.InvokeAsync(() => cut.Instance.NotifyKeyboardStep("e1", 0, -1, false));

        events[0].ResourceId.Should().Be("room-a");
        events[0].StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0));
    }

    [Fact]
    public async Task Announcement_Includes_The_Title_And_Repeats()
    {
        var events = OneEvent();
        var cut = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, events));
        var grid = cut.FindComponent<TmSchedulerTimeGrid>();

        await cut.InvokeAsync(() => grid.Instance.NotifyKeyboardStep("e1", 0, 1, false));
        cut.Find("[role='status']").TextContent.Should().Contain("Meeting");

        var first = cut.Find("[role='status']").TextContent;
        await cut.InvokeAsync(() => grid.Instance.NotifyKeyboardStep("e1", 0, -1, false));
        await cut.InvokeAsync(() => grid.Instance.NotifyKeyboardStep("e1", 0, 1, false));
        cut.Find("[role='status']").TextContent.Should().Be(first);
        cut.Find("[role='status']").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ShowTouchHint_Defaults_False_And_Renders_When_Opted_In()
    {
        var hidden = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, OneEvent()));
        hidden.FindAll(".tm-scheduler-touch-hint").Should().BeEmpty();

        var shown = Render<TmScheduler>(p => p
            .Add(c => c.View, TmScheduleViewType.Day)
            .Add(c => c.CurrentDate, new DateTime(2026, 10, 13))
            .Add(c => c.Events, OneEvent())
            .Add(c => c.ShowTouchHint, true));
        shown.Find(".tm-scheduler-touch-hint").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Draggable_Event_Describes_The_View_Move_Hint()
    {
        var cut = Render<TmSchedulerTimeGrid>(p => p
            .Add(c => c.Dates, [new DateOnly(2026, 10, 13)])
            .Add(c => c.Events, OneEvent()));

        var evt = cut.Find("[data-event-id='e1']");
        evt.GetAttribute("draggable").Should().Be("true");
        var hintId = evt.GetAttribute("aria-describedby");
        hintId.Should().NotBeNullOrEmpty();
        cut.Find($"#{hintId}").TextContent.Should().Contain("Alt+Up");
        evt.GetAttribute("aria-keyshortcuts").Should().Contain("Alt+ArrowDown");
    }

    [Fact]
    public void Move_Rejects_A_Date_That_Is_Not_Exact()
    {
        var evt = Evt("e1", "Meeting", new DateTime(2026, 10, 13, 9, 0, 0), new DateTime(2026, 10, 13, 10, 0, 0));
        SchedulerMove.Move(evt, "2026-10-14T00:00:00", 600, null, readOnly: false).Should().BeFalse();
        evt.StartLocal.Should().Be(new DateTime(2026, 10, 13, 9, 0, 0));
    }
}
