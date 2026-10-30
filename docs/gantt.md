# TmGantt

`TmGantt` (`src/Tempo.Blazor/Components/Scheduler/`) is the project chart: a task tree beside a
zoomable timeline, with dependencies, baselines, a workload view, filtering, import/export, history
and reports. The redesign (plan phases 1–8) split the former monolith into parts, moved every style
into its own `.razor.css`, repainted the chrome with the core tokens only, put the dialogs on the
core `TmModal`, made the chart measure **its own container**, and turned a mobile container into a
read-mostly surface. This file is the contract; the calendar it embeds for `GanttView.Calendar` is
[docs/scheduler.md](scheduler.md), and the shared pointer helpers are
[docs/touch-interaction.md](touch-interaction.md).

## Components of the family

| Component | Role |
|-----------|------|
| `TmGantt` | The chart. Hosts place only this. |
| `TmGanttPortfolio` | Portfolio of project cards, each embedding a toolbar-less `TmGantt` (mobile: a summary line and a mini-gantt); `AxisMode=Shared` aligns every card on one shared date axis with a sticky header. |
| `TmGanttWorkloadView` | The workload week (person rows, utilization bars); docked panel or `GanttView.Workload` view. |
| `TmGanttToolbar` / `TmGanttTree` / `TmGanttTimeline` / `TmGanttSidebar` / `TmGanttStatusChip` / `TmGanttPanelTabs` / `TmGanttViewSettings` / `TmGanttFilterBar` / `TmGanttMobileOverlays` / `TmGanttSummaryBand` | Internal parts of `TmGantt` (`[EditorBrowsable(Never)]`, public only because Razor cannot emit internal components). Never place them; they have JSON overlays so the doc guards stay closed. |
| `TmGanttTaskPanel` / `TmGanttReportsPanel` / `TmGanttHistoryList` / `TmGanttHistoryDrawer` / `TmGanttFilterPanel` | Right-hand / drawer surfaces the chart composes. |
| `TmGanttExportDialog` / `TmGanttImportDialog` / `TmGanttImportMappingDialog` | The export, import and column-mapping dialogs (core `TmModal`s). |
| `TmGanttExportPreview` | Internal (`[EditorBrowsable(Never)]`): the export dialog's built-in live preview — pure SVG/HTML miniature of the chosen format and options. |

## Setup

`gantt.js` is a classic script the host page must include. It resolves `pointer-drag.js` and
`pointer-gesture.js` from its own URL with a dynamic `import()`, so `_content/Tempo.Blazor/js/` must
be served **as a whole**; a per-file CSP hash must list both helper modules as well:

```html
<script src="_content/Tempo.Blazor/js/gantt.js"></script>
```

Without the helpers the chart still renders, scrolls and selects — only the pan, the dependency
drag, the long-press menu and the Alt+Arrow reorder are missing, and `gantt.js` logs a console
warning.
`window.tmGantt` exposes the interop surface: the scroll helpers
(`get/setScrollLeft|Top`, `getClientWidth`), the docked-workload scroll link (`linkScroll` /
`unlinkScroll`, idempotent, keyed off the workload element), the shared portfolio axis link
(`linkScrollGroup(groupId, elements)` / `unlinkScrollGroup(groupId)`, idempotent per group id;
pixel-mapped where the members' content widths match, a ratio of the scrollable distance where they
differ), the
history drawer focus return (`noteOpener` / `restoreOpener`), the virtual-scroll session
(`attachVirtualScroll(id, treeBody, timelineBody, dotNet, rowHeight, overscan)` — the plain-listener
vertical sync plus the window computation; `updateVirtualScroll(id, rowHeight, overscan, enabled)` follows
a row-height change or the mode gate, `remeasureVirtual(id)` re-measures after a data change and
`scrollToRow(id, index, align)` serves the treegrid keyboard) and the pointer sessions
`attachTimeline(id, timeline, body, treeBody, dotNet)` / `attachTree(id, treeBody, dotNet)` (plus the
matching `detach*`). Sessions are
keyed by a caller-supplied id — Blazor removes the element before `DisposeAsync` runs — idempotent,
and `attach*` resolve `true` only when every session registered, so a failed phase is retried on a
later render.

## Layout — the chart measures its own container

`LayoutMode` (`TmLayoutMode`, default `Auto`) replaced `ContainerWidth` (Breaking, phase 7):
`Auto` measures the Gantt's **own** container through `TmLayoutObserver` and classifies with the
shared `TmBreakpoints` — mobile below 640 px, tablet 640–1023 px, desktop from 1024 px. The root is
`div.tm-layout.tm-layout--no-container > div.tm-gantt`, carries `data-layout="desktop|tablet|mobile"`
and the `tm-gantt--desktop|tablet|mobile` class; `ResolvedLayoutChanged` fires when the resolved
mode changes (never for the initial value — do not pair it with `LayoutMode`).

```razor
<!-- a host that knew its width told the Gantt (before) -->
<TmGantt Items="tasks" ContainerWidth="900" />
<!-- the Gantt measures itself; pin a layout only for tests or forced presentations -->
<TmGantt Items="tasks" />
<TmGantt Items="tasks" LayoutMode="TmLayoutMode.Tablet" />
```

`ContainerWidth` still exists as an `[Obsolete]` alias: it keeps overriding the Fit-to-screen
measurement and, while `LayoutMode` is `Auto`, is classified like a measurement (900 → tablet).

| Layout | Presentation |
|--------|--------------|
| Desktop ≥ 1024 px | One wrapping `TmToolbar` row, the docked panels, the full zoom slider. |
| Tablet 640–1023 px | The toolbar is two rows (`.tm-gantt__toolbar--two-rows`, `Overflow=Menu` — a command that does not fit leaves into "More"), a narrower task tree, finger-sized targets, `TaskPanelMode=Auto` shows the task panel as a side sheet, and a coarse pointer gets the touch hint. |
| Mobile < 640 px | Read-mostly (below). |

A forced `LayoutMode` (or a forced cascading `TmLayoutContext` ancestor) renders that layout without
measuring. The chart root is a **CSS size container** (`container-type: inline-size`,
`container-name: tm-gantt`) and `position: relative`; every overlay (context menu, sheets, dialogs)
is a **sibling** of `.tm-gantt` inside the observer's `LayoutContent`, because a container is the
containing block of every fixed-position descendant and would confine and clip them.

### Mobile is read-mostly (decision G2)

A container that classifies as `Mobile` is a **reading surface**: viewing, changing **Status** and
**Percent**, and adding a task. The chrome is the compact toolbar — the full-width
Day | Week | Month segment plus chips (Sort by date | Fit to screen | Filter, with a dot while a
filter is active) — a bottom `TmMobileActionBar` (Add task | Expand all | Collapse all | More) and a
"More" bottom sheet (Zoom in / out, Task panel, History, Reports, View settings). Every right-hand
surface is a full-height `TmSidePanel` bottom sheet:

- the task detail is read-only except Status and Percent, with a sticky Cancel | Save footer, a
  localized "full editing" hint and the `TaskBreadcrumb` slot above the sheet title;
- adding a task is a minimal sheet (name, start, end);
- the filter editor is the dialog as a bottom sheet (44 px, two-line rows, sticky Apply);
- history is a read-only sheet; Reports are run-only cards.

**Not offered on mobile:** export, import, column mapping (absent from the chrome — a host that
opens their dialogs programmatically still gets them as fullscreen `TmModal`s), reorder (drag, Move
up / down, Alt+Arrow) and the dependency drag (no handles, rows `draggable="false"` without a
grip). The progress % shows beside every bar; the zoom slider is replaced by the zoom entries. A
host that wants the full editing chrome on a small screen pins
`LayoutMode="TmLayoutMode.Tablet"` (or Desktop). Enforced by bUnit tests with
`LayoutMode = TmLayoutMode.Mobile`; on a mobile container the Days zoom is pinned automatically
(unless the host set a zoom override) and the layout flip re-renders the parts through their own
render gates.

## Header

The header row (`div.tm-gantt__header`, `data-testid="gantt-header"`) renders only when at least
one of its slots is filled and `ShowHeader` is not `false`:

| Parameter | Purpose |
|-----------|---------|
| `Title` / `Subtitle` | Header title and the secondary line under it. |
| `HeaderStart` | Host content after the title (a project switcher, a breadcrumb). |
| `HeaderActions` | Host actions at the end of the row (Share, Save…) — the Gantt has no built-in header actions. |
| `ShowHeader` | `false` hides the row even when its slots are filled. Default `true`. |

```razor
<TmGantt Items="tasks" Title="Project Aurora" Subtitle="12 tasks · Jan–Mar">
    <HeaderStart><MyProjectSwitcher /></HeaderStart>
    <HeaderActions><TmButton Variant="ButtonVariant.Primary">Share</TmButton></HeaderActions>
</TmGantt>
```

## Toolbar

The toolbar is the core `TmToolbar` (`div.tm-gantt__toolbar > div.tm-toolbar.tm-gantt__toolbar-bar[role=toolbar]`)
with six labelled groups — `data-tm-group` = `create | structure | time | panels | data | settings`.
Hosts and tests key off `data-tm-group` and the unchanged `data-testid`s, never a positional
selector; a command that does not fit sits in the group's "More" menu (`.tm-toolbar-item--collapsed`
stays in the DOM, the menu entry carries `data-menu-testid`).

- `ToolbarLabels` (default `Icons`) maps onto `TmToolbar.Labels`: `Icons` keeps the icon-only row
  (the text stays the accessible name and the tooltip), `IconsWithText` shows every label, `Auto`
  shows them except on a toolbar narrower than 640 px.
- **Add task is a split button** (`data-testid="gantt-add-task-split"`): the primary half adds a
  task at the end, the chevron opens Add task above / below / add child task for the selected task
  (the same insert the context menu performs; disabled without a selection).
- `ShowQuickSearch` (default `false`) ends the toolbar with a "Search tasks…" box that writes a
  Title / Contains entry into `Filters` (raising `OnFiltersChanged`); emptying the box removes it.
  The search filters nothing on its own — it is sugar over the existing filter set.
- The structure group carries expand / collapse all, sort and critical path; the time group the
  Day | Week | Month segment and the zoom slider + its readout as ONE measured toolbar item — an
  Overflow=Menu row moves it into "More" as a whole (the slider's 50–200 % presets are the item's
  menu entries), so the readout can never wrap under the segment; the panels group opens the task
  panel, the workload panel, history and reports; the data group export and import; the settings
  group the column menu (`data-testid="gantt-column-menu"`, a `TmColumnPicker` over every rendered
  column except Title) and the view-settings menu (`gantt-view-settings-btn` — density, avatars,
  today marker, theme, WBS, notification settings).

Active filters render as removable chips (`TmFilterChip`) in `div.tm-gantt__filter-chips` under the
toolbar. `FilterPanelMode` picks the editor: `Inline` (default) is the strip under the toolbar,
`Dialog` a modal (a bottom sheet on a mobile viewport; footer **Clear all filters | Cancel |
Apply** — Apply raises the whole set, Cancel throws unapplied edits away, Clear all only clears the
rows being edited until Apply), `Auto` picks the dialog on a tablet or narrower container and the
strip on a desktop one. The value editor follows the field type (Status/Priority enum selects, date
inputs, Title text), and so do the operators.

## Summary band

`ShowSummaryBand` (default `false`; decision `GANTT-SUMMARY-SCOPE`) shows the rollup strip under the
tree + timeline — full width of the chart, above a docked workload, in the chart views (Day / Week /
Month / List) only. The summary is computed ONCE per data refresh from the **filtered** tasks — after
the filters and "show closed", but IGNORING the expand/collapse state (`GanttHelper.FlattenAll`), so
collapsing a parent never changes the numbers of the full-width strip below the tree. When that scope
is smaller than the data — a filter is active, or closed tasks are hidden while the data has some —
the band carries a localized "Filtered" note. Parents never contribute
their hours or cost a second time (only leaves add up).

| Card | Shows | Hidden when |
|------|-------|-------------|
| Status of tasks | `TmChart` Donut with the total in its hole + a legend with numbers (`role="img"` with a localized text summary) | never (any task) |
| Time spent | "128 h of 320 h" + a `TmProgressBar` (logged hours only without estimates) | no estimates and no logged hours |
| Milestones | "1 / 2" + a bar | no milestones |
| Budget overview | "320 000 CZK of 500 000 CZK" + a bar | no cost and no `BudgetTotal` |
| Work breakdown | "4 levels • 24 tasks" | no tasks |

Layout: a desktop container renders the five cards in one row (`repeat(auto-fit, minmax(…))`), a
tablet two columns, and the read-mostly mobile container a compact one-line strip
("24 tasks • 40 % done • 1 overdue") with a **Summary** entry in the "More" sheet that opens the full
cards as a bottom sheet. The section is `aria-label`ed, the card headings are `h3`, the donut is one
image for assistive tech, and status is never colour alone (the legend repeats every count).
`SummaryBandContent` (`RenderFragment<GanttSummary>`) replaces the built-in cards with host content
(the band itself stays the component's), `BudgetTotal` supplies the budget total and `BudgetCurrency`
a currency code appended through the active culture.

```razor
<TmGantt Items="tasks" ShowSummaryBand="true" BudgetTotal="500000m" BudgetCurrency="CZK" />
<TmGantt Items="tasks" ShowSummaryBand="true">
    <SummaryBandContent Context="summary"><MySummary Summary="@summary" /></SummaryBandContent>
</TmGantt>
```

The wireframe `TmGantt` stencil exposes the same switch as its `showSummaryBand` prop.

## Portfolio axis

`TmGanttPortfolio.AxisMode` (`GanttPortfolioAxisMode`, default `PerProject`) picks how the cards'
date axes align. `PerProject` keeps today's independent mini-gantts. `Shared` computes the union
range of **all** projects (collapsed cards included, so collapsing never shifts the axis), renders
ONE sticky axis header above the cards (`GanttHelper.BuildTimelineHeaderRows` over the portfolio's
`ZoomPreset`, default Weeks) and pins every card to it: `TimelineStart` / `TimelineEnd`, the same
`ZoomPreset` and one `TreeWidth` (the host's value, else 12rem) with `ShowTimelineHeader=false`. The
same date therefore draws at the same x in every card, and the header's horizontal scroll and the
cards' timelines scroll together through `tmGantt.linkScrollGroup(groupId, elements)` — entirely in
the browser, no interop per scroll tick. On a mobile container the shared band is the single monthly
strip above the mini-gantts (their percentages are of the union range; the per-card month rows hide,
and the whole band is `aria-hidden`).

The header is a flex row: a **leading cell** as wide as the pinned tree (plus the chart's own border)
holds the localized "Shared timeline" label, and the scrollable band to its right is the scroll
group's first member — the axis cells start exactly where every card's timeline starts, at every
viewport. `linkScrollGroup` maps equal content widths **pixel-for-pixel** (clamped to the target's
own range — the header's scroller is not as wide as a card's timeline) and falls back to the ratio of
the scrollable distance only where the content widths differ.

Shared mode **locks the cards' scale**: every card is forced to the desktop layout (a card that
classified as mobile would swap its zoom and the chrome), a pinned axis (`TimelineStart` /
`TimelineEnd`) makes `GetEffectiveZoomPreset` return the host's `ZoomPreset` — overriding the
Day | Week | Month segment and the mobile Days default — keeps the pixels per day at zoom 100, and
Ctrl+wheel zoom is ignored while pinned, so a card can never drift away from the header above it.

With `ShowTimelineHeader=false` the card's tree header collapses with the timeline's carrier:
visually hidden (the sr-only clip) while keeping `role="row"` and its `columnheader` cells, so the
first tree row stays level with the first bar and assistive tech keeps the treegrid header.

```razor
<TmGanttPortfolio Projects="projects" AxisMode="GanttPortfolioAxisMode.Shared" ZoomPreset="GanttZoomPreset.Months" />
```

The underlying `TmGantt` parameters are public: `TimelineStart` / `TimelineEnd` pin the axis and
override the data's range (bars and baselines outside it clip to the edge — a task that only touches
the range keeps its 1 px minimum, and a dependency whose endpoint clipped away entirely renders no
line; Fit to screen measures the pinned axis), `ShowTimelineHeader` (default
`true`) hides the two-row header without hiding the grid, and `TreeWidth` pins the task tree's
width. The wireframe `TmGanttPortfolio` stencil exposes `axisMode`.

## Views

`View` / `ViewChanged` (`GanttView`): `Day`, `Week` (default), `Month` render the tree +
timeline chart; `Workload` renders `TmGanttWorkloadView`; `List` the tree alone; `Calendar` the
embedded `TmScheduler` month; `Board` the `TmKanbanBoard` by status; `Dashboard` four stat widgets;
`People` the person rows with vacation overlays. The segment offers Day | Week | Month; the other
views come from the sidebar rail / tabs (`Workload`, `List`) or the view-settings menu.

`WorkloadPlacement` (`GanttWorkloadPlacement`, default `View`): `View` keeps the workload as the
alternate view; `Docked` renders it as a panel **under the timeline** (`gantt-workload-dock`) that
shares the Gantt's date range — `gantt.js linkScroll` keeps the two horizontal scrolls in step as a
ratio of the scrollable distance, entirely in the browser (no round trip per scroll event), skipped
with a localized note when the range exceeds the 366-day axis. `WorkloadDisplayMode` switches the
cells between hours and percentage.

## Task tree and keyboard

The tree is an ARIA treegrid: `div.tm-gantt__tree-grid[role=treegrid]`, a header `row` of
`columnheader`s, body `rowgroup`, each `.tm-gantt__tree-row[role=row]` with `aria-level`,
`aria-selected` and — for a parent — `aria-expanded`, its cells `role=gridcell`. Exactly one row has
`tabindex="0"` (the selected / last focused / first row), the rest `-1`.

| Key | Action |
|-----|--------|
| ↑ ↓ | previous / next visible row |
| Home / End | first / last row of the view |
| → | expand, then step into the first child (a leaf's → focuses its bar) |
| ← | collapse, then step to the parent |
| Enter (Space on key-up) | open the task panel |
| Alt+↑ / Alt+↓ | move the row up / down (`OnTaskReordered`, the WCAG 2.5.7 alternative to drag; not on a mobile container) |

The expand button and the bulk checkbox are fenced from the row's key handler (`tabindex="-1"`).
Bars and milestones are `role="button" tabindex="-1"` with an aria-label "title, start – end";
Enter / Space opens the panel, Esc / ← returns to the row. Every drag control has a non-drag path:
the context menu and the tabbed panel's **Move up / Move down** buttons (with `CanMoveUp` /
`CanMoveDown` edge disabling) perform the same reorder, and the task panel's Dependencies section
creates dependencies without a drag.

## Task panel, panels and sheets

`ShowTaskPanel` / `ShowTaskPanelChanged` (two-way; never assigned inside the component) shows the
inspector of the selected task: sections `plan | progress | structure | appearance | description |
attachments | comments | budget | timelog | dependencies` (`data-testid="task-section-*"`), a
**Status** draft that raises `OnStatusChanged` on Save, `TmDecimalInput` estimates / logged time /
budget, attachment cards, and Delete behind a confirm `TmDialog`. The footer is **Cancel | Save**,
outside the scroller (`task-cancel` / `task-save`).

`TaskPanelMode` (`GanttTaskPanelMode`, default `Inline`): `Inline` is the in-flow column at every
width; `Overlay` presents the panel — and the Reports inspector — as a core `TmSidePanel` side
sheet with the tabs **Detail | Attachments | Comments | History** (`TmGanttTaskPanel.Tabbed`;
the History tab lists the audit entries of the task; a draft on the Detail tab survives a tab
switch and is flagged "Unsaved changes"); `Auto` is `Inline` on a desktop container and the sheet
on a tablet or narrower one. With a sheet presentation **at most one right-hand surface is open**
(opening the task panel, Reports or the history drawer closes the other two), a finger or pen tap
on a row or bar opens the sheet (a mouse click just selects; Enter opens it from the keyboard), and
closing over a dirty draft asks first — "Discard changes?" / Discard / Keep editing.

The other panels: History (the shared `TmGanttHistoryList`, and the drawer with Time travel /
Rollback — `HistoryDrawerPlacement` `Component` (default, absolute inside the chart) or `Viewport`
(the pre-redesign full-height panel); rollback asks for confirmation first), Notifications (with
`NotificationSettings`, inside the view-settings menu since phase 2) and Reports (cards with Run /
Edit / Delete / Duplicate; "New report" renders only with `OnNewReportRequested`; the inspector
replaces the task panel while open). `SidebarMode` (`GanttSidebarMode`, default `Rail`) picks how
the panels are offered: the vertical icon rail on the left, or the `Tabs` strip
(`TmGanttPanelTabs`, ARIA tablist, `data-testid="panel-tab-tasks|workload|history|notifications|reports"`)
above the chart; `ShowSidebar=false` hides either.

## Zoom, timeline and dependencies

`ZoomPreset` (`GanttZoomPreset`: Hours, Days, Weeks (default), Months, Quarters, Years) drives the
axis; the zoom slider and the view-settings entries override it per session, Fit to screen measures
the timeline element in the browser (`gantt.js getClientWidth` — no `eval`, no global
`querySelector`). A pinned axis (`TimelineStart` / `TimelineEnd` set) locks the zoom: the effective
preset is the `ZoomPreset` parameter, the pixels per day stay at zoom 100 and Ctrl+wheel zoom is
ignored. `GanttViewSettings` carries the visual switches: `ViewDensity`
(`Comfortable` / `Compact` / `Touch` — 44 px rows with the tree and timeline sharing one height,
`data-density` on the root), `ShowTodayMarker` (the 2px solid line plus the localized "Today" pill),
`ShowProgressLabel`, `ShowAvatarsOnChart`, `ShowDaysOff`, `ShowClosedTasks`,
`ShowAdvancedContextButtons`, `TaskNameLocation`, `WbsInTitle` and `Theme` (`Auto` follows the
registered `ThemeService`; `Dark` / `Light` stamp `data-theme` on the chart root so the whole token
set applies — light islands included).

Dependencies render as SVG lines with the finger-sized `tm-gantt__dep-handle` hit areas when
`AllowDependencyCreation` is set; the drag commits once through `OnDependencyDragCompleted`.
`OnDependencyAdded` / `OnDependencyRemoved` are the host persistence path; the non-drag path is the
task panel's Dependencies section. `ShowCriticalPath`, `ShowOverdueHighlight`, `Baselines` /
`ActiveBaselineId` / `OnBaselineSaved` and the working-time model (`WorkingSchedule`,
`ResourceCalendars`, `AutoSchedule`) are unchanged pre-redesign surfaces.

## Export, import and mapping

Export (`gantt-export-btn`), import and column mapping open as core `TmModal`s (browser top layer,
`role="dialog"` + `aria-modal`, focus trap, Escape, focus back to the trigger, bottom sheet on a
mobile viewport). The per-dialog class (`tm-gantt__export-dialog` etc.) sits on the **overlay
root**; page-scoped locators, never `.tm-gantt`-scoped ones. Export offers the format radiogroup
(PDF / PNG / Excel / MS Project XML) with the PDF/PNG options and an Excel column picker, and a
**built-in live preview** that reacts to every option: `TmGantt` passes its visible (filtered)
tasks plus the dependencies between them (`PreviewTasks` / `PreviewDependencies` /
`PreviewCriticalPathIds`; `ShowBuiltInPreview` default `true`). PDF/PNG draw the miniature page in
the paper's aspect ratio (A4 / A3 / Letter, orientation) with bars, optional critical path, today
line and workload strip; Excel shows the first 20 rows of the selected columns (formatted by the
shared, culture-invariant `GanttColumnValues.Format`, the same helper `GanttXlsxExporter` uses);
XML shows task / dependency / milestone counts. The sketch is one image for assistive tech: the
root is `role="img"` with a localized summary label ("Preview: A4 landscape, 24 tasks, critical
path") over an `aria-hidden` body — on a fullscreen mobile dialog the collapsed `<details>` branch
keeps the same contract, wrapping the body in a `role="img"` element under the summary. A host's
own `PreviewContent` slot always wins; without `PreviewTasks` the neutral placeholder renders as
before.

Import is a tablist (Excel | MS Project | XML), an input group with **Choose file** / the chosen
name / **Remove file**, a supported-formats hint and a `TmAlert` error box; switching the Excel and
MS Project tabs drops the chosen file. On the Excel tab a result-capable GanttXlsx package reads
the sheet's headers first (`GanttExcelImporter.ReadHeaders`, which skips empty headers), confirms
the column mapping in the `TmGanttImportMappingDialog` (rendered as a sibling modal; seeded by the
dialog's `ImportMappings`) and then imports the full `GanttExcelImporter.ImportResult` — tasks,
dependencies, warnings. The component raises the events in the documented order:
`OnImportCompleted` carries the tasks first, then every new dependency is announced through
`OnDependencyAdded`, and `OnImportResultCompleted` closes the sequence with the full payload (the
dialog raises the same order for its own events). **Warnings never block the import**: the dialog
stays open and shows a `TmAlert` with the count plus an expandable list localized from the
warning's code and data ("Row 2: unknown predecessor '9'" — the row is the SHEET row, the header
row is 1); the list expands under a "Show details" summary, the chosen file is dropped and the
footer collapses to a single primary **Close** (no Cancel, no Import), so a completed import can
never be re-run from the same dialog. The mapping dialog pre-maps columns by header name, warns on
double targets, and **Import stays disabled until Task (title) and Start and End or Duration are
mapped**. An older GanttXlsx package (no `ImportResult`/`ReadHeaders`) keeps the direct import
without the mapping step. Wire the results through `OnExportRequested`,
`OnImportResultCompleted` / `OnImportCompleted` and `OnImportError`; wire `OnClose` to an explicit
close method — never a `Toggle*()` that the action callback also calls, or the dialog reopens
itself.

### Import data semantics (GanttExcelImporter)

- Task ids are stable per row within one import: `{options.IdPrefix}{n}`, `n` = the 1-based
  data-row ordinal. The default prefix is `xlsx-row-` (`xlsx-row-3`); the import dialog mints a
  fresh prefix per import run (`xlsx-{8 hex}-row-`), so importing the same sheet twice never mints
  colliding ids — pass your own `GanttExcelImportOptions.IdPrefix` when a host imports repeatedly.
  Predecessor row references use the bare ordinal (1 = the first data row); a dotted or lettered
  reference is a WBS code.
- All targets the mapping dialog offers are imported: Title, Start, End, Duration (invariant days;
  when End is unmapped or unparsable, End = Start + Duration), Progress, Status, Priority,
  Deadline → `DueDate`, Estimation / TimeLog → `EstimationHours` / `LoggedHours` (invariant),
  Assignees, Predecessor and WBS.
- Parsing is culture-invariant: dates accept `yyyy-MM-dd`, `dd.MM.yyyy`, `MM/dd/yyyy`,
  `yyyy-MM-ddTHH:mm:ss` and fall back to Excel serial numbers (OADate) for raw numeric cells;
  Progress accepts `NN`, `NN%` and the stored 0–1 fraction scale (`0.5` = 50 %, `1` = 100 % — type
  `NN%` to force an exact percent); hours and days use the invariant decimal point. A non-empty
  Start / End / Duration / Deadline / Estimation / TimeLog / Progress / Status / Priority cell
  that cannot be parsed is ignored and warns (`UnparsedValue` with row, column and raw value);
  an empty cell is "no data" and never warns.
- `WBS` → `ParentId`: the parent is the code without its last segment (`1.1.2` nests under `1.1`);
  rows resolve in a second pass, so order does not matter. A missing parent leaves the task a root
  and warns (`UnknownWbsParent`); a duplicated code warns (`DuplicateWbs`, first wins).
- Duplicate headers: a header used by several columns maps its FIRST column only and warns
  (`DuplicateHeader`); a host seeding `ImportMappings` with the same source column twice keeps the
  first mapping (never a `ToDictionary` crash).
- `Predecessors` → `GanttDependency`: tokens separated by `,` or `;`; an optional `FS|SS|FF|SF`
  suffix (case-insensitive, FS default) and a `±N[d]` lag. A token whose lag cannot be parsed
  (`3FS+2w`, `3+2 days`) is an unparsed value (`UnparsedValue`), not an unknown predecessor. The
  same (from, to, type) trio imports once; an edge that would close a cycle is skipped
  (`CircularDependency`); self references (`SelfDependency`) and unknown references
  (`UnknownPredecessor`) produce warnings, never exceptions.
- `Assignees` → people: split on `,`/`;`, trimmed, deduplicated case-insensitively. Without a
  resolver the normalized name is both `Id` and `Name`; `GanttExcelImportOptions.AssigneeResolver`
  turns a name into a real person, and a `null` answer skips the name.
- Sparse rows are read by `CellReference` column index, so a skipped cell cannot slide the columns
  that follow it. Every warning carries `Row` (the data ordinal predecessor references use) and
  `SheetRow` (the 1-based sheet row, from the row's `RowIndex`, for display).

## Large data sets (window virtualization)

`Virtualization` (`GanttVirtualizationMode` in Abstractions, default `Off`) renders only the visible window
of tree rows and timeline bars for large data sets. `Off` keeps the pre-virtualization DOM byte for byte;
`On` always virtualizes; `Auto` virtualizes once the visible set reaches `VirtualizationThreshold` visible
rows (default 300). `OverscanRows` (default 10) renders extra rows above and below the window so a scroll
step never shows an unpainted edge.

The window is computed **arithmetically from the fixed row height** (`RowHeight` — one value the tree and
the timeline share: 40 / 28 / 44 px per density and 56 px on the read-mostly mobile container), not with
`<Virtualize>`: two synchronized scroll containers with absolutely positioned bars and an SVG dependency
layer cannot host a framework virtualizer. The chart owns the window (`_windowStart` / `_windowEnd`); the
tree and the timeline only read it. The browser does the arithmetic — `gantt.js attachVirtualScroll`
computes the window in a `requestAnimationFrame` through the pure `gantt-virtual.mjs` maths (node-tested)
and reports `[JSInvokable] OnViewportRowsChanged(first, last, visibleRows)` only once the window moved past the
overscan/2-row **hysteresis**, so at most one .NET render happens per overscan/2 rows of scroll. The window
is anchored on the real pixel range the viewport shows — `firstVisible = floor(top / rowHeight)`,
`lastVisible = ceil((top + clientHeight) / rowHeight)`, the overscan added on both sides — so the partially
visible edge rows stay painted at a mid-row scroll, and the third report argument is the floor viewport row
count the keyboard paging (`PageRows`) uses. The same session owns the vertical tree↔timeline sync in the
browser — a plain scroll listener (`attachScrollSync`) that needs neither the maths module nor any interop;
the old interop round trip per scroll event (`OnTreeScroll` / `OnTimelineBodyScroll` →
`tmGantt.getScrollTop`/`setScrollTop`) and its C# handlers are gone. The session also follows its
environment on its own: a `ResizeObserver` on the tree body re-measures when the container resizes,
`tmGantt.remeasureVirtual(id)` re-measures (hysteresis cleared) after a data change — `TmGantt` calls it
whenever its `DrawVersion` moved — and `updateVirtualScroll(id, rowHeight, overscan, enabled)` silences the
window reports while the mode is inactive (the sync keeps running); a flip to enabled clears the hysteresis
and measures immediately, so a `Virtualization` / threshold crossing reports in the same tick.

**The window applies only while the session is attached** (`VirtualizationActive` = mode-active &&
attached): until gantt.js confirms itself the FULL set renders — prerender, bUnit and a host without the
script never hide a single row behind a window nobody computes. (The browser's own first paint is therefore
the full set too; the attach + first measurement correct it to the window immediately after.) While the
session is attached but not yet measured, the prerender window (the first `OverscanRows × 3` rows) stands.
The **List view** has no timeline: the session attaches the tree body as BOTH scrollers and reads the row
count from the tree body's own scroll height (the spacers keep it at `rows × RowHeight` in every window
state). The session re-registers whenever its key changes — the key carries the gesture key (the same
element-replacement signal the pointer sessions use), the row height and the overscan — so a DockedWorkload
toggle or a chart ↔ List flip never leaves it bound to detached elements. Without JS
(prerender, a missing script) the keyboard still pins what it focuses; a density or layout flip re-registers
the session with the new row height.

What renders per window: tree rows (window + two `aria-hidden` spacers of the exact remaining height, so
the scroll area and the timeline keep the full `rows × RowHeight`), bars, baseline ghosts, progress labels
and the dependency handles. A dependency line renders when its **y-range intersects the window**, even with
both ends outside it. The grid and the non-working overlays are horizontal (x-axis) — the demo's 10 000-task
data set spans ~330 days ≈ 47 week cells + ~94 weekend rects, far below the 2 000-cell threshold where a
repeating-gradient or a scrollLeft window would pay off, so they stay as-is. The rows a keyboard user or a
gesture still needs render **pinned** outside the window with their absolute `aria-rowindex`: the roving
tab stop, the selection, the inline edit, the dragged row and the drop target, plus a bar the focus request
is about to reach. The treegrid keeps its a11y contract while virtualized: `aria-rowcount` = visible nodes
+ header and an absolute `aria-rowindex` on every row. Home / End / PageUp / PageDown move through the
window (the page is what the viewport really shows — the browser-reported floor of the body height over
the row height; 15 rows without virtualization), the target row renders pinned and the JS session
scrolls it into view (`scrollToRow`), announced as "Row {0} of {1}". The scrollable region of the axis is
`.tm-gantt__timeline-body` (`tabindex="0"`, `role="region"`, the localized `TmGantt_TimelineAxis` label,
a `:focus-visible` ring) — the pan container above it is not a tab stop.

Scope: the virtualization covers the chart views (Day / Week / Month / List) at every layout — desktop,
tablet and the read-mostly mobile container (56 px rows, native finger scroll, long-press on a
re-rendered row). Calendar, Board, Dashboard, People, the workload and the summary band are unaffected —
the band, the docked workload and the export preview always compute over the whole (filtered) data, never
over the window. A nested `TmGantt` inside `TmGanttPortfolio` virtualizes only when the host passes
`Virtualization` to the portfolio. **Browser Ctrl+F finds only rendered rows** — a host that needs
in-chart search points users at `ShowQuickSearch` (which filters the data instead).

| 10 000 tasks, all expanded (WASM `/gantt-large`, Chromium) | `Off` | `Auto` |
|---|---|---|
| First render (navigation → rows painted) — the FULL set in both modes since the attach gate | ~31.3 s | ~30.4 s |
| Window bound applied after that paint (`Auto_windowAppliedMs` − `Auto_firstRenderMs`) | — | ~0.3 s |
| `.tm-gantt__tree-row` elements once the window applied | 10 000 | 28 |
| Elements inside `.tm-gantt` | ~428 000 | ~1 600 |
| .NET renders while scrolling in 200 px steps (5 rows) | — | 1 per step (the overscan/2 hysteresis — the phase target) |
| Long tasks > 50 ms / worst / heap over 50 full-viewport scrolls | 47 / 223 ms / +5.0 MB | 50 / 313 ms / −23.0 MB |

The dev-box targets of the phase (the window bounding the DOM ≤ 1.5 s WASM / ≤ 800 ms Server after the
first paint — `Auto_windowAppliedMs − Auto_firstRenderMs` in the report, since the attach gate makes the
first paint the full set in both modes; scroll without long tasks > 50 ms) are written against quiet
hardware; the table records the measured numbers of this build (the WASM lane also pays the deterministic
10 000-task generation before the first paint, which the busy CI runner stretches into the tens of
seconds in BOTH modes).
The live report is written by
`GanttVirtualizationE2ETests.Performance_TenThousandTasks_Meets_The_Targets` to
`tests/Tempo.Blazor.E2E/TestResults/gantt-phase12/perf.json`.

```razor
<!-- a 10 000-task project -->
<TmGantt Items="tasks" Virtualization="GanttVirtualizationMode.Auto" />
<!-- small charts stay byte-identical: Off is the default, no spacers, no aria-rowcount -->
<TmGantt Items="tasks" />
```

## Touch and pointer

All pointer work is `gantt.js` on the shared core helpers — never a second drag implementation:

| Gesture | Session |
|---------|---------|
| Timeline pan | **mouse only** (a finger or a pen scrolls natively; `touch-action: pan-x pan-y` is CSS-owned and applies to the pen too). Threshold 4, Escape restores; scrollLeft/Top are written in JS with no interop per move. |
| Dependency drag | every pointer type; a ghost `<svg>` line, the bar under the pointer highlighted (`tm-gantt__bar--dep-target`), edge auto-scroll via `createAutoScroller`, hit-tested release committed once. |
| Long-press (500 ms, finger) | the context menu on bars and tree rows, anchored at the finger; the lifting finger's click does not dismiss it. On a mobile container the menu is the read-mostly limited menu (Status / Percent / Add task). |
| Tree reorder (keyboard) | `attachKeyboardMove`: Alt+↑/↓ → `OnTreeKeyboardMove`, with an `accept` predicate that leaves Alt+arrow inside an inline editor to the text field. |
| No HTML5 drag under a finger | a touch pointer sets `draggable="false"` on the pressed row (restored on release); mouse and pen keep HTML5 drag-and-drop. |

Escape, `pointercancel` or a lost capture rolls a gesture back; `attachTimeline` / `attachTree`
resolve `true` only after the sessions registered.

## CSS and theming contract

Every `TmGantt*` component owns its scoped `.razor.css` — subcomponent styles never live in the
parent's stylesheet (`ScopedCssOwnershipTests`). The gantt CSS uses only `--tm-*` tokens: zero
colour literals, zero undefined aliases (a strict token audit guards this); dark mode, the indigo
theme and a host's overrides arrive through the tokens, and `GanttViewSettings.Theme` stamps
`data-theme` for a light / dark island. The global dialog stylesheet of the pre-TmModal era
(`_gantt-dialog.css`) was removed. Reduced-motion subjects are enumerated in
`wwwroot/css/animations.css`; a new animated subject must join them.

## Testing notes

- Pin layouts with `LayoutMode="TmLayoutMode.Desktop|Tablet|Mobile"` in bUnit and E2E — the real
  browser classifies the demo Gantt as tablet at the default 1280 viewport (~956 px of container).
- Overlays (context menu, sheets, dialogs) are siblings of `.tm-gantt`: query them page-scoped.
- Legacy desktop specs should pin a 1440×900 viewport and wait for `data-layout` before
  interacting; a helper that taps a command "on the bar or in More" survives overflow changes.
- Visual baselines: `GanttVisualBaselineE2ETests` pins the phase-2 look per (view × theme); re-record
  consciously and note it in the CHANGELOG.
