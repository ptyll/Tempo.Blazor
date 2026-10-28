# TmScheduler

`TmScheduler` (`src/Tempo.Blazor/Components/Scheduler/`) is the calendar. It renders Month, Week, Day,
Timeline and Agenda, and it decides the visible range in one place (`SchedulerRange`), so the loaded
range, the rendered range and the toolbar title are the same range.

| View | Range | Navigation step | Title |
|------|-------|-----------------|-------|
| Day, Timeline | that single day | one day | the day |
| Week | seven days from `FirstDayOfWeek` (`WorkWeek`: five) | seven days | the week |
| Month | the rendered grid (42 days, or 35 when `MonthWeeks="Auto"` drops an empty trailing week) | one month | the month |
| Agenda | `AgendaDays` days from `CurrentDate` (default 30, minimum 1) | `AgendaDays` days | the inclusive range |

`CurrentDate` picks the range. It defaults to today in the display zone, not `DateTime.Today`.

## Setup

`scheduler.js` is a classic script the host page must include. It loads `pointer-drag.js` and
`pointer-gesture.js` itself (a dynamic `import()` resolved from its own URL), so no second script tag
is needed, but both files must be served from the same `_content/Tempo.Blazor/js/` folder:

```html
<script src="_content/Tempo.Blazor/js/scheduler.js"></script>
```

A Content-Security-Policy that hashes scripts per file must list `pointer-drag.js` and
`pointer-gesture.js` as well. The contract is [touch-interaction.md](touch-interaction.md).

## Toolbar

The toolbar is the core `TmToolbar` (`Overflow="Menu"`). Groups carry `data-tm-group` and hosts key
off that, never a positional selector:

| Group | Contents |
|-------|----------|
| `header-start` | `HeaderStart`, only when the host supplied it |
| `navigation` | Today, Previous, Next |
| `title` | the range; the trigger of the date popover (`role="button"`, `aria-haspopup="dialog"`) |
| `view` | the view switcher (`TmTabs`, pill). The body is the matching `tabpanel` |
| `header-actions` | `HeaderActions`, only when supplied |
| `filter` | the resource filter, only with `ShowResourceFilter` |
| `primary` | New event, only when `OnAddClick` has a delegate |
| `export` | Print, inside the More menu |

Stable test ids: `scheduler-prev`, `scheduler-next`, `scheduler-today`, `scheduler-print`,
`scheduler-add`, `scheduler-view-menu`, `scheduler-resource-filter`, `scheduler-close-detail`.

**Tablet is one row** (decision SCH-TABLET-ONE-ROW). The bar does not wrap; what does not fit goes
to More. The bar needs `flex: 1 1 auto; width: 100%; min-width: 0` — `TmToolbar` is a size container,
and a flex item with `flex-basis: auto` collapses to zero, which shows More at every width.

**Mobile** (container under 640px) replaces that bar with a compact row: `‹ title ›`, a view
dropdown, and a Today icon button only while `MobileActionBar` is `None`. The resource filter, when
shown, is its own second row, and its popup is the bottom sheet.

## Layout

`LayoutMode="Auto"` (the default) classifies the scheduler's **own container**:

| Width | Layout |
|-------|--------|
| ≥ 1024px | desktop: the full toolbar, the docked side panel |
| 640–1023px | tablet: one toolbar row, the panel as a side sheet |
| < 640px | mobile: the compact toolbar, a week date strip in Week, a resource picker in Day, the panel as a bottom sheet |

A forced `LayoutMode` (or a forced cascading `TmLayoutContext`) renders that layout without measuring.
Print pins the layout back to desktop, so a phone prints the full week — call `PrintAsync()` rather
than relying on Ctrl+P, whose `beforeprint` re-render is async.

The root is `height: 100%`. Give it an ancestor with a height; an unbounded host grows with the day.
On a mobile layout the time-grid scroller is capped at `70dvh`.

## Side panel

`SidePanel` and `EventDetailTemplate` are host slots rendered through the core `TmSidePanel`
(`Presentation="Auto"`). The scheduler never invents the body — no attendees, no RSVP, no categories.

```razor
<TmScheduler Events="_events"
             Resources="_resources"
             ShowResourceFilter="true"
             @bind-SelectedResourceIds="_resourceIds"
             @bind-SelectedEvent="_selected"
             @bind-SidePanelOpen="_open"
             SidePanelTitle="Calendar">
    <SidePanel>
        <TmCalendarView Compact="true" SelectedDate="@_day" OnDateClick="PickDay" />
    </SidePanel>
    <EventDetailTemplate Context="evt">
        <h3>@evt.Title</h3>
        <p>@evt.StartLocal.ToString("g")</p>
        <p>@evt.Description</p>
    </EventDetailTemplate>
</TmScheduler>
```

An event click opens the detail when `EventDetailTemplate` is set. Closing the panel — Escape, the
close button, or the backdrop — returns focus to that event through the scheduler's own
root-scoped `TempoBlazor.Scheduler.focusRovingStop`. The detail block carries its own
`TmScheduler_CloseDetail` button, which clears only the detail: a host `SidePanel` stays open.
`SidePanelTitle` names the docked header when no detail is showing; null falls back to
`TmSidePanel_DefaultTitle`.

The resource filter (`ShowResourceFilter`, default `false`) is the core `TmMultiSelect`. An empty
`SelectedResourceIds`, and a list whose ids match no known resource, keep every event, so a stale
selection hides nothing. An event with no resource stays visible.

## Ranges, hours, and the current time

```razor
<TmScheduler View="TmScheduleViewType.Week"
             @bind-CurrentDate="_date"
             FirstDayOfWeek="1"
             WeekDays="TmSchedulerWeekDays.WorkWeek"
             WorkDayStart="new TimeOnly(8, 0)"
             WorkDayEnd="new TimeOnly(17, 0)"
             ShowFullDay="false"
             HighlightOffHours="true"
             ScrollToTime="new TimeSpan(8, 0, 0)"
             ShowNowIndicator="true" />
```

- `ShowFullDay="false"` draws only the working hours in Day, Week and Timeline. `HighlightOffHours`
  tints the hours outside them and is opt-in.
- `ScrollToTime` scrolls that time into view after the first render. Null does not scroll.
- `ShowNowIndicator` (default `true`) draws the current-time line in today's column. The line is its
  own component (`SchedulerNowIndicator`) driven by an injected `TimeProvider` and the display zone,
  so a tick re-renders the line and not the grid. Tests pin the clock with `FakeTimeProvider`.
- `MonthWeeks="Auto"` drops an empty trailing week; `Six` (the default) keeps a stable height.
- `WeekDays="WorkWeek"` hides the weekend. `HighlightWeekends` is opt-in.
- `AgendaDays` is the length Agenda loads, renders and steps by.

## Mobile action bar

Opt-in. `MobileActionBar="None"` (the default) renders nothing, and the compact toolbar keeps its
Today button. `Auto` renders the core `TmMobileActionBar` on a mobile layout only — Today moves into
the bar — and stays hidden on tablet and desktop.

```razor
<TmScheduler MobileActionBar="TmSchedulerMobileActionBar.Auto"
             LayoutMode="TmLayoutMode.Auto" />
```

`ShowTouchHint` (default `false`) renders a localized hint under the toolbar, visible only on
`(hover: none) and (pointer: coarse)`.

## Keyboard alternative to drag

Every drag has a non-drag path (WCAG 2.5.7). A plain arrow is the roving key of the grid; the move
chords use Alt, so they never collide with it. A read-only event is not a keyboard target.

| View | Move | Resize |
|------|------|--------|
| Day, Week | Alt+Left/Right moves a day, Alt+Up/Down a slot | Alt+Shift+Up/Down resizes the end |
| Timeline | Alt+Left/Right moves a slot, Alt+Up/Down another resource | Alt+Shift+Left/Right resizes the end |
| Month | Alt+Left/Right moves a day, Alt+Up/Down a week | none |

The month grid is `role="grid"` with one roving tab stop: arrows move a day or a week, Home/End the
row, PageUp/PageDown raise `CurrentDateChanged`, and Enter or Space select the day
(`SelectedDateChanged`, `OnSlotClick`). Only the "+N more" button opens the Day view. Events are
buttons. A move is announced through the scheduler's polite live region (`TmScheduler_EventMoved`,
`TmScheduler_EventResized`).

Touch: a mouse or a pen press is HTML5 drag-and-drop; a finger holds for 400 ms and then drags
(`pointer-drag.js`, `pointerTypes: ['touch']`). Resize uses the same helper on
`.tm-scheduler-event-resize-handle`. The time grid renders a top and a bottom handle, the timeline
a left and a right handle, and the keyboard resize (Alt+Shift+Arrow) changes the end only. A coarse
pointer shows the 44px handle on the selected or focused event, and a compact selected event hides
the start grip. A second finger does not drive the gesture, and Escape, `pointercancel` or a lost
capture rolls it back. Both gestures commit through the same path as the HTML5 drop.

## Events and the "+N more" link

`EventTemplate` replaces the inner content of an event in every view. The positioning wrapper and the
resize handle stay, and the template must not contain its own focusable element — the wrapper is
already the button. On the timeline the default time and title spans stay direct children of the
card; `span.tm-scheduler-event-content` is a host opt-in.

The month overflow is a button (`.tm-scheduler-month-more`). Its visible label is the plural family
`TmScheduler_More` (`Loc.Plural`): English "+1 more / +2 more", Czech "+1 další / +2 další /
+5 dalších". The accessible name (`TmScheduler_MoreAria`) names the day it opens. Without
`OnMoreClick` the scheduler opens that day in the Day view, but only when the host binds both
`@bind-View` and `@bind-CurrentDate`; with either binding missing the click is inert.

`MaxVisibleEvents` (default 3) is the desktop and tablet cap; a mobile layout uses
`CompactMaxVisibleEvents` (default 1).

## Time zone

`TimeZoneId` (an IANA id such as `Europe/Prague`, or a Windows id) wins. Without it the scheduler
uses the circuit's `ITmUserTimeZone` — the browser's zone, UTC on Blazor Server until the probe
reports, the runtime's zone on WebAssembly — and the machine-local zone only when no
`ITmUserTimeZone` is registered. `ResolveTimeZone()` returns the effective zone. Recurrence expansion
and the "today" of the now indicator both use it; neither reads `DateTime.Now`.

## Gantt tablet and touch

The embedded calendar mode of `TmGantt` renders this scheduler; the Gantt's own tablet layout, the `TaskPanelMode` side sheet and the
pointer gestures of `gantt.js` (pan, dependency drag, long-press menu, Alt+Up/Down reorder) are described in
[touch-interaction.md](touch-interaction.md) and in the `CHANGELOG` entry "TmGantt phase 7".
