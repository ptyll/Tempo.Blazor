# TmScheduler

`TmScheduler` (`src/Tempo.Blazor/Components/Scheduler/`) is the calendar. It renders Month, Week, Day,
Timeline and Agenda, and it decides the visible range in one place (`SchedulerRange`), so the loaded
range, the rendered range and the toolbar title are the same range.

| View | Range | Navigation step | Title |
|------|-------|-----------------|-------|
| Day, Timeline | that single day | one day | the day |
| Timeline with `TimelineScale="Days"` | the week (seven days from `FirstDayOfWeek`, `WorkWeek`: five) | seven days | the week |
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
| `view` | the view switcher (pill `TmTabs`; a toolbar under 60rem of its own width swaps it for the compact view dropdown). The body is the matching `tabpanel` |
| `header-actions` | `HeaderActions`, only when supplied |
| `filter` | the resource filter, only with `ShowResourceFilter` |
| `primary` | New event, only when `OnAddClick` has a delegate |
| `export` | Print, inside the More menu |

Stable test ids: `scheduler-prev`, `scheduler-next`, `scheduler-today`, `scheduler-print`,
`scheduler-add`, `scheduler-view-menu`, `scheduler-resource-filter`, `scheduler-close-detail`.
`scheduler-view-menu` names the view-switcher region in BOTH presentations (the pill group or the
compact dropdown wrapper — they never render together) and `scheduler-view` names the dropdown; the
pills carry `data-view="<enum>"`, and so do the dropdown options. The hidden pills stay in the DOM
(`display:none`), so a test switches views by clicking the pill when it is visible and otherwise
opening `scheduler-view-menu` and picking the `data-view` option.

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
- `ScrollToTime` scrolls that time into view after the first render. Null does not scroll. The
  day-granularity timeline has no minute axis, so there it scrolls the **first event's day column**
  into view instead; the `0 – 24 h` range knobs (`WorkDayStart`, `WorkDayEnd`, `ShowFullDay`,
  `HighlightOffHours`) and the now indicator do not apply to that scale.
- The full (desktop/tablet) toolbar keeps the date title above the view buttons: when the bar is
  narrower than 60rem of its OWN container, the pill view switcher collapses to the same dropdown the
  phone's compact row uses, so the active view is never clipped and the title never shrinks to
  nothing. The switch is a `@container tm-toolbar` query, not a viewport breakpoint.
- `ShowNowIndicator` (default `true`) draws the current-time line in today's column. The line is its
  own component (`SchedulerNowIndicator`) driven by an injected `TimeProvider` and the display zone,
  so a tick re-renders the line and not the grid. Tests pin the clock with `FakeTimeProvider`.
- `MonthWeeks="Auto"` drops an empty trailing week; `Six` (the default) keeps a stable height.
- `WeekDays="WorkWeek"` hides the weekend. `HighlightWeekends` is opt-in.
- `AgendaDays` is the length Agenda loads, renders and steps by.

## Day-granularity timeline (channels × days)

`TimelineScale` turns the Timeline view from one day of hour slots into the days of the range with one
row per resource — the "channel × day" board a campaign planner needs. `Hours` is the default and the
shape every earlier version rendered; nothing changes unless a host opts in.

```razor
<TmScheduler View="TmScheduleViewType.Timeline"
             TimelineScale="TmSchedulerTimelineScale.Days"
             @bind-CurrentDate="_week"
             Events="_channelEvents"
             Resources="_channels"
             FirstDayOfWeek="1"
             WeekDays="TmSchedulerWeekDays.WorkWeek"
             ScrollToTime="new TimeSpan(8, 0, 0)" />
```

- **Range, title, navigation, provider.** The scale is part of `SchedulerRangeOptions`, so the loaded
  week (`VisibleEvents`/`DataProvider` query), the toolbar title and Previous/Next all follow the
  Week view: the range is `FirstDayOfWeek`'s seven days (`WorkWeek`: five), a step is seven days and
  the title is the inclusive week. The two views cannot drift apart, because one `SchedulerRange`
  call feeds both.
- **Rows and columns.** One row per resource (the sticky label column) and one column per day
  (weekday + date header). A narrow container scrolls horizontally and the resource column stays
  pinned — the same scroller the hour timeline uses (`data-tm-scroll="timeline-days"`), so there is
  no forced degradation to Agenda at any width.
- **Chips.** A day cell paints one chip per event with the localized time and the title (or
  `EventTemplate`, whose content replaces the inner markup exactly as in the hour timeline, with a
  visually hidden accessible name). An all-day event prints `TmScheduler_AllDay`. An event that
  started on an earlier day keeps the `tm-scheduler-timeline-day-event--continuation` dashed edge and
  paints on every day it covers.
- **Lanes.** Overlapping events inside one day take the shared `SchedulerLayout` lanes: the cell is a
  CSS grid of `--tm-scheduler-lanes` columns, each chip is placed by `grid-column` (its lane) and
  `grid-row` (its order inside the lane), and `data-lane` names the lane for hosts and tests. A cell
  narrower than 12rem stacks its chips chronologically instead of splitting them into slivers (the
  lane is still on every chip as `data-lane` / `--tm-scheduler-lane`), because a 60 px chip cannot
  read. Two overlapping events therefore become side-by-side only where there is room for two.
- **Cap.** `MaxVisibleEvents` (default 3) caps a cell; unlike a month cell, a day cell keeps that cap
  on a mobile layout too, because a 7.5rem column of short chips is not a cramped month cell. The rest becomes the localized plural `TmScheduler_More` button (English "+1 more", Czech
  "+3 další"), whose accessible name is `TmScheduler_MoreAria`. It raises `OnMoreClick` — and the
  scheduler's own fallback opens that day in the Day view when the host binds both `@bind-View` and
  `@bind-CurrentDate`, exactly like the month grid.
- **Tokens.** The column width is `--tm-scheduler-timeline-day-width` (9rem; 7.5rem on a mobile
  layout) and the cell floor is `--tm-scheduler-timeline-day-cell-height` (4.5rem). On a mobile
  layout the channel column is 8rem — wider than the hour timeline's 5rem rail — and the channel
  name wraps instead of ellipsizing to one letter. The header and
  the rows size themselves from those tokens, so a host overrides the board's density with two
  declarations. Both are component-local tokens on `.tm-scheduler`. The board also paints
  **scroll-edge shadows**: a right edge while columns still hide to the right, and a shadow on the
  pinned channel column's right edge once content has scrolled under it — both only while the board
  actually overflows (`scheduler.js` keeps `data-scroll-shadow` on the frame: `none`, `start`,
  `end` or `both`, so a board whose columns fit shows no edge at all). Themes through
  `--tm-scheduler-scroll-shadow-size` and `--tm-scheduler-scroll-shadow-color` on the frame.
- **Lifecycle.** The board measures its own container (the scheduler's `TmLayoutObserver`), prints
  every column on one page (the pin unpins and the scroller stops scrolling in `@media print`; the
  "+N more" cap still applies, and the button prints as its own text), and
  the chips are a touch drag surface: a finger holds and drags, the drop lands on a day and a
  resource, and the event keeps its time of day (the commit passes `-1` for the minute axis, the same
  value a month drop passes). A chip that paints a **continuation** day is not the event's first
  day, so the commit moves the start by the days the pointer crossed (`StartLocal.Date +
  (dropDay − grabbedDay)`, the grabbed day travels as the fifth `NotifyMoveComplete` argument and as
  `scheduler.js`'s `session.startDate`) — a four-day campaign stays four days long instead of
  collapsing its start onto the dropped cell.

## Event time in a narrow column

A week cell, a week of resources and a phone all end up narrower than the full range
("9:00 AM – 10:00 AM"). The default renderer prints the range twice — a full reading and a compact
one that shares one meridiem ("9:00 – 10:00 AM") — and the stylesheet picks by the **card's own
width** (`@container`). A time never breaks between the clock and its meridiem: `SchedulerTimeText`
normalizes ICU's narrow no-break space to U+00A0, so the only break opportunities in a full range
are the two plain spaces around the en dash — a narrow card wraps the range onto a second line
("10:00 AM –" / "12:00 PM") instead of tearing "10:00 AM" apart. The markup is two spans,
`.tm-scheduler-event-time-full` and `.tm-scheduler-event-time-compact`, inside the wrapper that
carries the full range in its `title`:

| Card width | What the card prints |
|---|---|
| ≥ 8rem | the full reading, which wraps instead of being cut mid-token |
| 4.5 – 8rem | the compact reading |
| < 4.5rem | no time (the title and the card's accessible name keep it) |

The wrapper's `title` always carries the full range, so a hover (or a screen reader reading the
card's name) still gets it. `EventTemplate` opts out of all three — a host template owns its own
layout.

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
| Timeline, `Days` scale | Alt+Left/Right moves a day, Alt+Up/Down another resource | Alt+Shift+Left/Right changes the end by a day |
| Month | Alt+Left/Right moves a day, Alt+Up/Down a week | none |

The day board is `role="grid"` too — a row per resource, a `gridcell` per day — with **one tab stop
per grid**: the active cell. Arrows cross days and rows, Home/End the row, and Space selects the
whole day (`OnSlotClick` with `00:00 – 23:59:59`). Enter (or F2) steps *into* the cell: its first
chip becomes the grid's tab stop (a chip is `tabindex="-1"` until then, so a board with thirty events
is still one stop), ArrowUp/ArrowDown walk the cell's own widgets (the visible chips and then the
"+N more" button), Escape returns to the cell, and Enter/Space on a chip opens it. Its move hint is
`TmScheduler_MoveHint_TimelineDays`, and a move announces the **date** (the scale has no minute
axis), not the time.

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
`@bind-View` and `@bind-CurrentDate`; with either binding missing the click is inert. The
day-granularity timeline's per-day cap uses the same control (`.tm-scheduler-timeline-day-more`) and
the same fallback.

`MaxVisibleEvents` (default 3) is the desktop and tablet cap; a mobile layout uses
`CompactMaxVisibleEvents` (default 1).

## Time zone

`TimeZoneId` (an IANA id such as `Europe/Prague`, or a Windows id) wins. Without it the scheduler
uses the circuit's `ITmUserTimeZone` — the browser's zone, UTC on Blazor Server until the probe
reports, the runtime's zone on WebAssembly — and the machine-local zone only when no
`ITmUserTimeZone` is registered. `ResolveTimeZone()` returns the effective zone. Recurrence expansion
and the "today" of the now indicator both use it; neither reads `DateTime.Now`.

## Gantt tablet and touch

The embedded calendar mode of `TmGantt` renders this scheduler. The Gantt's full contract —
container layout (`LayoutMode`), the tablet two-row toolbar, `TaskPanelMode` side sheet, the
pointer gestures of `gantt.js` (pan, dependency drag, long-press menu, Alt+Up/Down reorder) and the
read-mostly mobile surface — is [docs/gantt.md](gantt.md); the shared helpers are
[touch-interaction.md](touch-interaction.md), and the CHANGELOG entries "TmGantt phase 7" /
"TmGantt phase 8" list the breaking details.

## Gantt mobile (read-mostly)

A Gantt container that classifies as mobile is a **reading surface** (decision G2): one pinned Title column with a two-line cell
(title over the date range, 56 px rows in the tree and the timeline), the full-width Day | Week | Month segment and
chips (Sort by date | Fit to screen | Filter, with a dot for an active filter), a bottom `TmMobileActionBar`
(Add task | Expand all | Collapse all | More) and a "More" bottom sheet (Zoom in / out, Task panel, History, Reports,
View settings). Every right-hand surface is a full-height `TmSidePanel` bottom sheet: the task detail is read-only
except **Status** and **Percent** (sticky Cancel | Save; the `TaskBreadcrumb` slot renders above the title), adding a
task is a minimal sheet (name, start, end), the filter editor is the dialog (two-line rows, 44 px targets, sticky
Apply), history is read-only and Reports are run-only cards. Export, import, column mapping, reorder (drag, Move
up / down, Alt+Arrow) and the dependency drag are **not offered** on a mobile container — a host that wants the full
editing chrome pins `LayoutMode="TmLayoutMode.Tablet"`. `TmGanttPortfolio` (mobile cards with a mini-gantt) and
`TmGanttWorkloadView` (the week person list with a utilization bar and the pinned person column) follow the same
`LayoutMode` their host passes. Full contract: [gantt.md](gantt.md).
