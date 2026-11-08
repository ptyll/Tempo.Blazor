using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The day-granularity half of <see cref="TmSchedulerTimelineView"/>: the same resource rows, but the
/// columns are the days of the range (a channel × day board) instead of the hour slots of one day.
/// The hour markup and its move contract are untouched; this partial owns the day columns, the chip
/// placement, the "+N more" cap and the day/keyboard move path. The two halves share one component
/// (and one JS registration) because a host switches the granularity on one view, not on two.
/// </summary>
public partial class TmSchedulerTimelineView
{
    private IReadOnlyList<DateOnly> _days = [];
    private readonly Dictionary<(string ResourceId, DateOnly Day), IReadOnlyList<TimelineDayItem>> _dayCells = [];
    private int _activeDayIndex;

    /// <summary>
    /// The "+N more" button the reader stepped onto, as <c>resource|yyyy-MM-dd</c>, or null. The grid
    /// has ONE tab stop: either its active cell, the focused chip or this button.
    /// </summary>
    private string? _focusMoreCell;

    /// <summary>
    /// How the columns are laid out: the hour slots of <see cref="CurrentDate"/> (the default) or the
    /// days of the range with one row per resource. <see cref="TmSchedulerTimelineScale.Days"/> loads,
    /// renders and steps the week the Week view renders.
    /// </summary>
    [Parameter] public TmSchedulerTimelineScale Scale { get; set; } = TmSchedulerTimelineScale.Hours;

    /// <summary>The day the day columns start on. 0 is Sunday, 1 (the default) Monday.</summary>
    [Parameter] public int FirstDayOfWeek { get; set; } = 1;

    /// <summary>Which days the day columns show. <see cref="TmSchedulerWeekDays.WorkWeek"/> drops the weekend.</summary>
    [Parameter] public TmSchedulerWeekDays WeekDays { get; set; } = TmSchedulerWeekDays.FullWeek;

    /// <summary>
    /// The chip cap of one day cell. The overflow becomes the localized "+N more" button, which raises
    /// <see cref="OnMoreClick"/>. Default 3.
    /// </summary>
    [Parameter] public int MaxVisibleEvents { get; set; } = 3;

    /// <summary>Fires when the "+N more" button of a day cell is pressed, with that day.</summary>
    [Parameter] public EventCallback<DateTime> OnMoreClick { get; set; }

    /// <summary>The day the day columns mark as today. The default marks no column.</summary>
    [Parameter] public DateOnly Today { get; set; }

    /// <summary>The selected day. The matching cell carries <c>aria-selected</c> and a modifier class.</summary>
    [Parameter] public DateTime? SelectedDate { get; set; }

    /// <summary>True when the columns are days instead of hour slots.</summary>
    private bool IsDays => Scale == TmSchedulerTimelineScale.Days;

    private string RovingStopSelector => _focusMoreCell is { } moreCell
        ? $".tm-scheduler-timeline-day-more[data-cell=\"{moreCell}\"]"
        : _focusEventId is null
            ? IsDays
                ? ".tm-scheduler-timeline-day-cell[tabindex='0']"
                : ".tm-scheduler-timeline-slot[tabindex='0']"
            : $"[data-event-id=\"{_focusEventId}\"]";

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private int DayIndex(DateOnly day)
    {
        for (var i = 0; i < _days.Count; i++)
            if (_days[i] == day) return i;
        return -1;
    }

    /// <summary>The dates the day columns render, and the chips of every resource × day cell.</summary>
    private void BuildDays()
    {
        _days = SchedulerRange.WeekDates(CurrentDate, FirstDayOfWeek, WeekDays);
        _dayCells.Clear();
        if (_days.Count == 0) return;

        foreach (var resource in _resources)
        {
            // The row's whole event set, NOT GetEventsForResource: that one clips to the visible
            // hour range of CurrentDate, which would drop every other day of the board.
            var rowEvents = _resourceEventMap.TryGetValue(resource.Id, out var list)
                ? list
                : (IReadOnlyList<TmScheduleEvent>)[];
            foreach (var day in _days)
                _dayCells[(resource.Id, day)] = SchedulerTimelineDay.Layout(rowEvents, day);
        }

        if (_activeDayIndex < 0 || _activeDayIndex >= _days.Count) _activeDayIndex = 0;
        // A range that moved away from the selected day keeps the roving stop on the first column.
        if (SelectedDate is { } selected && DayIndex(DateOnly.FromDateTime(selected)) is var at && at >= 0)
            _activeDayIndex = at;
    }

    private IReadOnlyList<TimelineDayItem> DayCellItems(string resourceId, DateOnly day)
        => _dayCells.TryGetValue((resourceId, day), out var items) ? items : [];

    private bool IsActiveDayCell(string resourceId, DateOnly day)
        => ResourceIndex(resourceId) == _activeResourceIndex && DayIndex(day) == _activeDayIndex;

    /// <summary>The key the "+N more" button and the focused-cell selector are found by.</summary>
    private static string CellKey(string resourceId, DateOnly day) => $"{resourceId}|{Iso(day)}";

    /// <summary>The grid's one tab stop is the active cell — unless a chip or a "+N more" holds it.</summary>
    private bool IsDayCellStop(string resourceId, DateOnly day)
        => IsActiveDayCell(resourceId, day) && !HasFocusedDayEvent && _focusMoreCell is null;

    /// <summary>A chip is a tab stop only when the reader stepped into its cell (Enter or F2).</summary>
    private bool IsDayEventStop(TmScheduleEvent evt) => _focusEventId == evt.Id;

    private bool IsDayMoreStop(string resourceId, DateOnly day)
        => _focusMoreCell == CellKey(resourceId, day);

    /// <summary>A focus target that is no longer rendered (the event left the range) must not eat the stop.</summary>
    private bool HasFocusedDayEvent
        => _focusEventId is { } id && Events is not null && Events.Any(e => e.Id == id);

    /// <summary>The chips a cell paints, capped exactly like the markup caps them.</summary>
    private IReadOnlyList<TimelineDayItem> VisibleDayItems(string resourceId, DateOnly day)
        => SchedulerTimelineDay.Split(DayCellItems(resourceId, day), MaxVisibleEvents).Visible;

    /// <summary>How many chips the cell's "+N more" hides.</summary>
    private int RemainingDayItems(string resourceId, DateOnly day)
        => SchedulerTimelineDay.Split(DayCellItems(resourceId, day), MaxVisibleEvents).Remaining;

    /// <summary>The cell a widget was reached from becomes the roving cell, so Escape and the arrows land there.</summary>
    private void ActivateDayCell(string resourceId, DateOnly day)
    {
        _activeResourceIndex = ResourceIndex(resourceId);
        var at = DayIndex(day);
        if (at >= 0) _activeDayIndex = at;
    }

    private void FocusDayEvent(string resourceId, DateOnly day, TmScheduleEvent evt)
    {
        ActivateDayCell(resourceId, day);
        _focusMoreCell = null;
        _focusEventId = evt.Id;
        _moveFocus = true;
    }

    /// <summary>Steps onto the cell's last widget: the capped "+N more" button.</summary>
    private void FocusDayMore(string resourceId, DateOnly day)
    {
        ActivateDayCell(resourceId, day);
        _focusEventId = null;
        _focusMoreCell = CellKey(resourceId, day);
        _moveFocus = true;
    }

    /// <summary>Escape returns to the cell, which is the grid's own tab stop again.</summary>
    private void FocusDayCell(string resourceId, DateOnly day)
    {
        ActivateDayCell(resourceId, day);
        _focusEventId = null;
        _focusMoreCell = null;
        _moveFocus = true;
    }

    private bool IsToday(DateOnly day) => Today != default && day == Today;

    private bool IsSelectedDay(DateOnly day)
        => SelectedDate is { } selected && DateOnly.FromDateTime(selected) == day;

    private bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>Monday-to-Friday columns are marked so a host can wash the weekend without a prop.</summary>
    private string DayHeaderClass(DateOnly day)
        => IsToday(day) ? "tm-scheduler-timeline-day-header--today" : "";

    private string DayCellClass(DateOnly day)
    {
        var css = "";
        if (IsToday(day)) css += " tm-scheduler-timeline-day-cell--today";
        if (IsWeekend(day)) css += " tm-scheduler-timeline-day-cell--weekend";
        if (IsSelectedDay(day)) css += " tm-scheduler-timeline-day-cell--selected";
        return css;
    }

    private string DayCellLabel(DateOnly day, int count)
        => SchedulerA11y.DayCellLabel(Loc, day, count, CultureInfo.CurrentUICulture);

    /// <summary>An all-day event has no time of day, so the cell prints the localized "all day".</summary>
    private (string Full, string Compact) TimeText(TmScheduleEvent evt)
        => evt.AllDay
            ? (Loc["TmScheduler_AllDay"], Loc["TmScheduler_AllDay"])
            : SchedulerTimeText.Range(evt.StartLocal, evt.EndLocal);

    private RenderFragment DayEvent(TmScheduleResource resource, TimelineDayItem item, DateOnly day) => builder =>
    {
        var e = item.Event;
        var mutable = !ReadOnly && !e.IsReadOnly;
        var time = TimeText(e);
        var sequence = 0;

        builder.OpenElement(sequence++, "div");
        builder.SetKey(e.Id);
        builder.AddAttribute(sequence++, "class",
            $"tm-scheduler-timeline-day-event tm-scheduler-event-surface{(item.IsContinuation ? " tm-scheduler-timeline-day-event--continuation" : "")}{(IsSelectedEvent(e) ? " tm-scheduler-timeline-event--selected" : "")} {e.CssClass}");
        if (IsSelectedEvent(e)) builder.AddAttribute(sequence++, "aria-current", "true");
        builder.AddAttribute(sequence++, "role", "button");
        // A chip is not a tab stop of its own: the cell is the grid's one stop until Enter/F2 steps in.
        builder.AddAttribute(sequence++, "tabindex", IsDayEventStop(e) ? "0" : "-1");
        builder.AddAttribute(sequence++, "data-tm-roving", _rovingId);
        builder.AddAttribute(sequence++, "data-event-id", e.Id);
        builder.AddAttribute(sequence++, "data-lane", item.Lane.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(sequence++, "style",
            SchedulerTimelineDay.CellStyle(item) + EventStyle(e.Color ?? resource.Color));
        builder.AddAttribute(sequence++, "title", e.Title);
        builder.AddAttribute(sequence++, "draggable", mutable ? "true" : "false");
        if (EventTemplate is null) builder.AddAttribute(sequence++, "aria-label", EventAriaLabel(e));
        if (mutable)
        {
            builder.AddAttribute(sequence++, "aria-describedby", _moveHintId);
            builder.AddAttribute(sequence++, "aria-keyshortcuts",
                "Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown Alt+Shift+ArrowLeft Alt+Shift+ArrowRight");
        }

        // The grabbed cell travels with the drag: a continuation chip is not the event's first day.
        builder.AddAttribute(sequence++, "ondragstart", EventCallback.Factory.Create(this, () => HandleDayDragStart(e, resource.Id, day)));
        builder.AddAttribute(sequence++, "onclick", EventCallback.Factory.Create(this, () => HandleEventClick(e)));
        builder.AddEventStopPropagationAttribute(sequence++, "onclick", true);
        builder.AddAttribute(sequence++, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, k => HandleDayEventKeyDown(k, e, resource.Id, day)));
        builder.AddEventStopPropagationAttribute(sequence++, "onkeydown", true);
        builder.AddAttribute(sequence++, "onkeyup", EventCallback.Factory.Create<KeyboardEventArgs>(this, k => HandleEventKeyUp(k, e)));
        builder.AddEventStopPropagationAttribute(sequence++, "onkeyup", true);
        builder.AddAttribute(sequence++, "oncontextmenu", EventCallback.Factory.Create<MouseEventArgs>(this, args => HandleEventContextMenu(args, e)));
        builder.AddEventPreventDefaultAttribute(sequence++, "oncontextmenu", true);
        builder.AddEventStopPropagationAttribute(sequence++, "oncontextmenu", true);

        if (EventTemplate is not null)
        {
            builder.OpenElement(sequence++, "span");
            builder.AddAttribute(sequence++, "class", "tm-sr-only");
            builder.AddContent(sequence++, EventAriaLabel(e));
            builder.CloseElement();
            builder.AddContent(sequence++, EventTemplate(e));
        }
        else
        {
            // The full reading wraps in a narrow column and the compact one fits one line; the
            // stylesheet picks by the chip's own width, and the title carries the full text.
            builder.OpenElement(sequence++, "span");
            builder.AddAttribute(sequence++, "class", "tm-scheduler-timeline-event-time");
            builder.AddAttribute(sequence++, "title", time.Full);
            builder.AddAttribute(sequence++, "aria-hidden", "true");
            builder.OpenElement(sequence++, "span");
            builder.AddAttribute(sequence++, "class", "tm-scheduler-event-time-full");
            builder.AddContent(sequence++, time.Full);
            builder.CloseElement();
            builder.OpenElement(sequence++, "span");
            builder.AddAttribute(sequence++, "class", "tm-scheduler-event-time-compact");
            builder.AddContent(sequence++, time.Compact);
            builder.CloseElement();
            builder.CloseElement();

            builder.OpenElement(sequence++, "span");
            builder.AddAttribute(sequence++, "class", "tm-scheduler-timeline-event-title");
            builder.AddContent(sequence++, e.Title);
            builder.CloseElement();
        }

        builder.AddElementReferenceCapture(sequence, r => _eventRefs[e.Id] = r);
        builder.CloseElement();
    };

    // ── Day cell interaction ──

    /// <summary>A day cell is a whole day: 00:00 to 23:59:59, so a click lands without a minute axis.</summary>
    private async Task HandleDayCellClickAsync(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        await OnSlotClick.InvokeAsync((start, day.ToDateTime(new TimeOnly(23, 59, 59))));
    }

    private async Task HandleDayCellKeyDown(KeyboardEventArgs e, string resourceId, DateOnly day)
    {
        if (SchedulerA11y.HasModifier(e)) return;
        if (_days.Count == 0) return;

        // Enter or F2 steps INTO the cell: the first chip becomes the grid's tab stop, because a chip
        // is not one of its own (one tab stop per grid). Escape on a chip returns here.
        if ((e.Key == "Enter" || e.Key == "F2") && !e.Repeat)
        {
            var visible = VisibleDayItems(resourceId, day);
            if (visible.Count > 0)
            {
                FocusDayEvent(resourceId, day, visible[0].Event);
                return;
            }
        }

        var index = ResourceIndex(resourceId) * _days.Count + DayIndex(day);
        var target = RovingGrid.Move(index, e.Key, _days.Count, _resources.Count * _days.Count);
        if (target >= 0)
        {
            _activeResourceIndex = target / _days.Count;
            _activeDayIndex = target % _days.Count;
            _moveFocus = true;
            return;
        }

        if (SchedulerA11y.IsEnterActivation(e)) await HandleDayCellClickAsync(day);
    }

    private Task HandleDayCellKeyUp(KeyboardEventArgs e, DateOnly day)
        => SchedulerA11y.IsSpaceActivation(e) ? HandleDayCellClickAsync(day) : Task.CompletedTask;

    /// <summary>
    /// A chip's keys. Enter/Space open it, Escape returns to its cell, and Up/Down walk the cell's own
    /// widgets (the visible chips and then the "+N more" button) — the one tab stop is the cell, so a
    /// reader reaches a chip from there rather than by tabbing through every event on the board.
    /// </summary>
    private async Task HandleDayEventKeyDown(KeyboardEventArgs e, TmScheduleEvent evt, string resourceId, DateOnly day)
    {
        if (SchedulerA11y.HasModifier(e)) return;
        switch (e.Key)
        {
            case "Escape":
                FocusDayCell(resourceId, day);
                return;
            case "ArrowDown":
                FocusSiblingDayWidget(resourceId, day, evt, forward: true);
                return;
            case "ArrowUp":
                FocusSiblingDayWidget(resourceId, day, evt, forward: false);
                return;
        }
        if (SchedulerA11y.IsEnterActivation(e)) await HandleEventClick(evt);
    }

    private void FocusSiblingDayWidget(string resourceId, DateOnly day, TmScheduleEvent current, bool forward)
    {
        var visible = VisibleDayItems(resourceId, day);
        var at = -1;
        for (var i = 0; i < visible.Count; i++)
            if (visible[i].Event.Id == current.Id) { at = i; break; }
        if (at < 0) return;

        if (forward)
        {
            if (at + 1 < visible.Count) FocusDayEvent(resourceId, day, visible[at + 1].Event);
            else if (RemainingDayItems(resourceId, day) > 0) FocusDayMore(resourceId, day);
        }
        else if (at > 0)
        {
            FocusDayEvent(resourceId, day, visible[at - 1].Event);
        }
    }

    /// <summary>The "+N more" button's keys: Up goes back to the last chip, Escape back to the cell.</summary>
    private void HandleDayMoreKeyDown(KeyboardEventArgs e, string resourceId, DateOnly day)
    {
        if (SchedulerA11y.HasModifier(e)) return;
        switch (e.Key)
        {
            case "ArrowUp":
            {
                var visible = VisibleDayItems(resourceId, day);
                if (visible.Count > 0) FocusDayEvent(resourceId, day, visible[^1].Event);
                return;
            }
            case "Escape":
                FocusDayCell(resourceId, day);
                return;
        }
    }

    private async Task HandleDayCellDropAsync(DateOnly day, string targetResourceId)
    {
        if (_draggedEvent is null) return;
        // The event moves by the days the pointer crossed, so a chip grabbed on a continuation day
        // carries its whole multi-day span along instead of collapsing onto the dropped day.
        var date = DayMoveDate(_draggedEvent, day, _draggedFromDay);
        if (await CommitMoveAsync(_draggedEvent, date, -1, targetResourceId))
        {
            _draggedEvent = null;
            _draggedEventOriginalResourceId = null;
            _draggedFromDay = null;
        }
    }

    private Task HandleDayCellContextMenu(MouseEventArgs e, DateOnly day, string resourceId)
        => HandleSlotContextMenu(
            e,
            day.ToDateTime(TimeOnly.MinValue),
            day.ToDateTime(new TimeOnly(23, 59, 59)),
            resourceId);

    private Task HandleDayMoreClickAsync(DateOnly day) => OnMoreClick.InvokeAsync(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// The Alt+Arrow alternative on the day scale: Left/Right moves a day, Up/Down another resource,
    /// and a shifted Left/Right changes the end by a day (an event may span several columns).
    /// </summary>
    private async Task KeyboardStepDaysAsync(TmScheduleEvent evt, int x, int y, bool shift)
    {
        if (ReadOnly || evt.IsReadOnly) return;

        if (shift)
        {
            if (x == 0) return;
            var endLocal = evt.EndLocal.AddDays(x);
            if (endLocal <= evt.StartLocal) endLocal = evt.StartLocal.AddMinutes(1);
            var zone = TimeZone ?? TimeZoneInfo.Local;
            evt.End = new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal));
            await OnEventChanged.InvokeAsync(evt);
            await OnAnnounced.InvokeAsync(SchedulerMove.ResizedText(Loc, evt));
            return;
        }

        if (x == 0 && y == 0) return;
        var date = DateOnly.FromDateTime(evt.StartLocal.Date.AddDays(x));
        await CommitMoveAsync(evt, Iso(date), -1, ResourceAt(evt, y));
    }

    /// <summary>The moved chip is the roving stop, so a keyboard move keeps the focus on the event.</summary>
    private void SyncActiveDay(TmScheduleEvent evt)
    {
        if (!IsDays) return;
        var at = DayIndex(DateOnly.FromDateTime(evt.StartLocal));
        if (at >= 0) _activeDayIndex = at;
    }
}
