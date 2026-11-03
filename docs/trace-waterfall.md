# TmTraceWaterfall

A generic span waterfall for traces: one row per span, indented by its place in the parent tree, a bar
measured against one shared time axis, the duration and any host-declared metric columns.

```razor
@using Tempo.Blazor.Components.Observability
@using Tempo.Blazor.Models

<TmTraceWaterfall Spans="@spans"
                  Title="@Loc["TraceTitle"]"
                  MetricColumns="@columns"
                  LiveMode="true"
                  @bind-SelectedSpanId="_selectedSpanId">
    <DetailTemplate Context="span">
        <h4>@span.Name</h4>
        <p>@span.Kind — @span.Status</p>
    </DetailTemplate>
</TmTraceWaterfall>
```

It ships in the core package (`Tempo.Blazor`), the models in `Tempo.Blazor.Abstractions` — so an API
can build the exact objects the waterfall renders.

## The model

`TraceSpan` is data only:

| Member | Type | Notes |
|---|---|---|
| `Id` | `string` | Required, unique. The parent link, the selection key and the row key. |
| `ParentId` | `string?` | `null` = a root. A value naming no supplied span = an **orphan**. |
| `Name` | `string` | Falls back to `Id` when blank, so a row is never nameless. |
| `Kind` | `TraceSpanKind` | `Llm`, `Tool`, `Agent`, `Http`, `Db`, `Custom`. Drives the icon and the colour. |
| `StartedAt` | `DateTimeOffset` | The axis starts at the earliest start of the trace. |
| `EndedAt` | `DateTimeOffset?` | `null` = not finished: the bar extends to "now". Earlier than `StartedAt` = zero length. |
| `Status` | `TraceSpanStatus` | `Ok`, `Error`, `Running`, `Cancelled`. `Running` is the only state the live ticker serves. |
| `Metrics` | `IReadOnlyDictionary<string, double>` | `tokens_in`, `tokens_out`, `cost`, … summed per subtree. |
| `Attributes` | `IReadOnlyDictionary<string, string>` | Free-form, for `DetailTemplate` and tooling. Never interpreted. |

The two default maps are ONE shared instance wrapped in `ReadOnlyDictionary`, so a caller that casts the
interface back to a dictionary gets a `NotSupportedException` instead of silently changing the default of
every span in the process.

A trace is supplied as a **flat list**. The tree is derived from `ParentId`:

- a blank `ParentId` is a root;
- a `ParentId` that names no supplied span is an **orphan**, attached under the root and labelled with
  the localized "Orphan" chip — its data is never silently dropped;
- a parent **cycle** (A→B→A, or a span parented to itself) is **rejected**: no row is rendered for its
  members, their children are re-attached as orphans, and `TraceWaterfallLayout` reports the rejected
  ids instead of throwing or recursing forever;
- a blank `Id` is skipped and a repeated `Id` keeps its **first** occurrence (a child naming that id
  resolves to the first span, so dropping the duplicate is the only deterministic reading).

## Axis, bars and the slowest span

The axis runs from the earliest `StartedAt` to the latest effective end (`EndedAt`, or "now" when it is
missing). Each bar's offset and width are percentages of that window, clamped into it; a span entirely
outside a zoomed window keeps its row but paints no bar (`IsVisible = false`), so zooming never hides
data.

Ticks use "nice" steps (aiming for three intervals, so the labels never collide in a narrowed timeline column) and the unit the range reads in: **ms** under a second, **s** under a minute,
**min** above. Numbers are culture-formatted (`0,2 s` in Czech, `0.2 s` in English); the unit label is
localized (`TmTraceWaterfall_UnitMs` / `_UnitSeconds` / `_UnitMinutes`).

The **slowest span** badge searches **leaf** spans only. The root's duration is the whole request and an
intermediate span's is its children's envelope, so highlighting either would just point at the trace
itself. Only a trace where every span has a child (a single path) falls back to the longest row.
Ties keep the pre-order first.

## Metric columns and subtree sums

```csharp
private IReadOnlyList<TraceMetricColumn> Columns =
[
    new("tokens_in", "Tokens in", "N0"),
    new("cost", "Cost", "C2")   // Format is a .NET numeric format string
];
```

A column whose key no span reports renders a dash (`—`), never a zero — "not reported" and "reported as
zero" stay distinguishable. A span with children also shows the **subtree sum** (`Σ`) behind a localized
accessible name ("Subtree total: 15"), so "this call" and "this call and everything under it" are both
visible. The sum is computed over the whole subtree, each descendant counted once.

## Zoom

`+`, `−` and reset are native buttons with localized `aria-label`s and `aria-keyshortcuts`. The same keys
work from a focused row: `+`/`=` zooms in, `−`/`_` zooms out, `0` resets. They are handled on the grid,
not on the component root, so typing `+` inside a host's `DetailTemplate` never zooms the trace. Zooming
narrows the visible window around its centre; `ShowZoom="false"` removes the group. The toolbar summary
always reports the FULL trace duration, so zooming never changes what the trace *is*.

## Selection and the detail panel

`SelectedSpanId` is two-way bound (`@bind-SelectedSpanId`); a row is selected by click, Enter or Space and
gets `aria-selected="true"`. The disclosure button toggles the subtree **without** selecting, so the two
actions never ride on one gesture.

`DetailTemplate` receives the `TraceSpan`. The panel sits to the **right** of the grid on a wide
container and **below** it on a narrow one — a container query on the component's own root
(`--tm-trace-waterfall-detail-width`, the `1024px` container threshold), never the viewport, so a narrow card in a wide
page still stacks.

## Scrolling and narrow containers

The treegrid is the **one scroll owner in both axes**. Its track minimums (name, timeline, duration and
one track per metric column) can easily exceed a 360–390 px container, so:

- every row and the header keep the full template (`min-inline-size: max-content`) and the grid scrolls
  **sideways** instead of collapsing its tracks;
- the header is `position: sticky` at the top of that same scrollport, so it stays visible while the rows
  scroll vertically AND moves with them horizontally — the header can never drift away from its columns;
- `MaxHeight` (default `420px`) caps the grid, not a separate row box, so the sticky header is counted in
  the height it guards;
- the overflow is contained by the grid (`min-inline-size: 0; overflow: auto`), so the page itself never
  scrolls sideways on a phone.

A `@container tm-trace-waterfall (width < 640px)` query tightens the track sizes through the
`--tm-trace-waterfall-name-width`, `--tm-trace-waterfall-timeline-width`,
`--tm-trace-waterfall-duration-width` and `--tm-trace-waterfall-metric-width` knobs. The component's inline
`--tm-trace-waterfall-columns` template references those variables (the count of metric tracks varies per
host), so the component never writes a pixel size and the narrow band needs no second template.

## Live mode

```razor
<TmTraceWaterfall Spans="@runningTrace" LiveMode="true" LiveTickInterval="@TimeSpan.FromSeconds(1)" />
```

With `LiveMode` the component creates an `ITimer` from the supplied `TimeProvider` (default
`TimeProvider.System`) and redraws every `LiveTickInterval` through `InvokeAsync(StateHasChanged)` —
**only while some span is running**; a tick with nothing running redraws nothing and never writes to the
live region. The timer is disposed in `IDisposable`, and a tick that loses the race with `Dispose` is a
no-op. A non-positive `LiveTickInterval` falls back to one second.

Tests drive this with `FakeTimeProvider`, which is the supported way to prove the running bar grows
without waiting:

```csharp
var clock = new FakeTimeProvider(start);
var cut = Render<TmTraceWaterfall>(p => p
    .Add(c => c.Spans, spans)
    .Add(c => c.LiveMode, true)
    .Add(c => c.TimeProvider, clock));

clock.Advance(TimeSpan.FromSeconds(1));   // one tick → the running bar is wider
```

## Accessibility

- **Treegrid semantics:** `role="treegrid"` with `row`/`columnheader`/`gridcell` children,
  `aria-rowcount`/`aria-colcount`, `aria-label` (the `AriaLabel` parameter, else the title, else the
  localized default), `aria-level` and `aria-expanded` on every row, `aria-selected` on the selected one.
- **Roving tabindex:** exactly one row carries `tabindex="0"`. ↑/↓ move it, Home/End jump to the first and
  last visible row, → expands a collapsed row (or moves to the first child), ← collapses an expanded row
  (or walks to its parent). Focus follows the roving index through `ElementReference.FocusAsync` — no
  custom JavaScript; a row that the virtualizer removed in the meantime simply keeps the tabindex move.
- **Activation:** rows are `div`s, so they emulate activation. Enter selects on keydown (guarded against
  auto-repeat) and Space selects on keyup, exactly where a native `<button>` fires its click. The
  disclosure is a native `<button>` that stops propagation — one activation per press, never two. See
  [keyboard-activation-convention.md](keyboard-activation-convention.md).
- **No default scroll on the keys it handles:** ↑/↓/←/→, Home/End and Space would scroll the page (and the
  row area) by default, which pushes the row being navigated out of view. `@onkeydown:preventDefault` is
  a single render-time bool per element, so switching it on would also swallow Tab and trap focus inside
  the grid. The decision therefore lives in the `tm-trace-waterfall.js` ES module, which listens for
  `keydown` on the grid and cancels exactly those keys when the event targets a ROW — the native
  disclosure button, a control a host might place inside a row, Tab, Enter and the `+`/`−`/`0` zoom
  shortcuts keep their browser behaviour. The module is imported on demand (`TraceWaterfallInterop`, a
  `LazyJsModule`), so no `<script>` tag is needed and a host without JavaScript loses only the scroll
  suppression, not the keyboard support.
- **Colour is never the only carrier:** each `Kind` has its `--tm-trace-*` colour, its icon AND a
  visually hidden kind name; a non-OK status renders an icon plus localized text; the slowest leaf
  carries the localized "Slowest" badge.
- **One polite live region** for the whole component, written only from a user action (select,
  expand/collapse, zoom). A live tick never announces.
- **Forced colors:** every bar and its track keep a `CanvasText` border and the selected row keeps a
  persistent `Highlight` outline, so neither the bar's extent nor the selection depends on a background
  colour the UA replaces. `prefers-reduced-motion: reduce` clamps the bar's width/offset transition.

## Localization keys

`TmTraceWaterfall_AriaLabel`, `_Actions`, `_ColumnName`, `_ColumnTimeline`, `_ColumnDuration`, `_Empty`,
`_Summary`, `_Expand`, `_Collapse`, `_Slowest`, `_Orphan`, `_Selected`, `_Expanded`, `_Collapsed`,
`_Zoomed`, `_ZoomIn`, `_ZoomOut`, `_ZoomReset`, `_ZoomWindow`, `_Detail`, `_SubtreeTotal`, `_KindLlm`,
`_KindTool`, `_KindAgent`, `_KindHttp`, `_KindDb`, `_KindCustom`, `_StatusOk`, `_StatusError`,
`_StatusRunning`, `_StatusCancelled`, `_UnitMs`, `_UnitSeconds`, `_UnitMinutes`.

All four resource files (`TmResources.json`, `.cs`, `.sk`, `.fr`) carry every key with identical
placeholders.

## CSS classes

`.tm-trace-waterfall`, `__title`, `__toolbar`, `__summary`, `__zoom`, `__zoom-btn(--reset)`, `__window`,
`__layout`, `__grid`, `__head`, `__head-cell(--name|--timeline|--duration|--metric)`, `__axis`,
`__tick(--mark|--label)`, `__scroll`, `__empty`, `__row(--selected|--error|--slowest)`, `__name`,
`__toggle`, `__toggle-spacer`, `__kind(--llm|--tool|--agent|--http|--db|--custom)`, `__label`,
`__status(--error|--running|--cancelled)`, `__slowest`, `__orphan`, `__timeline`, `__track`,
`__bar(--llm|--tool|--agent|--http|--db|--custom)`, `__duration`, `__metric`, `__metric-cell`,
`__metric-value`, `__metric-sum`, `__metric-none`, `__detail`.

The kind colours are `--tm-trace-llm`, `--tm-trace-tool`, `--tm-trace-agent`, `--tm-trace-http`,
`--tm-trace-db` and `--tm-trace-custom`, declared in `tokens.css` and in BOTH blocks of
`tokens-dark.css` (dark palette and the `[data-theme="light"]` island). `__layout` is a grid with one
column; `@container tm-trace-waterfall (width >= 1024px)` gives it the detail column. The only animated
subject is `.tm-trace-waterfall__bar`, enumerated in the `animations.css` reduced-motion block. The grid
scrolls on both axes and the head is sticky over the scrolling rows; `__scroll` is only the ARIA rowgroup
(no overflow of its own). The scroll-key guard lives in `wwwroot/js/tm-trace-waterfall.js` (`attachScrollKeyGuard`
/ `detachScrollKeyGuard`).

## Wireframe

The `trace-waterfall` UI role (`BuiltInUiRoleVocabularySource`) plus the `TmTraceWaterfall` schema
(`title`, `expanded`, `showZoom`, `showMetrics`, `live`) and its declarative stencil in
`tempo.stencil.json`, which draws the nested rows, the zoom controls, the duration column and the live
badge conditionally.

## Demo

A `/data-display` section builds a generated agent run — a root run, two LLM calls, two tool calls, two
HTTP leaves, a DB leaf and one failed request — with the host metric columns, a selected span's detail
and a live-trace toggle. The full `/trace-waterfall` page (a ~150-span generated trace) lands with the
plan's phase 9, which is carried forward.

## Layout helpers

The pure maths lives in `TraceWaterfallLayout.Build(spans, now, window)` (internal, surface-tested by
`TraceWaterfallLayoutTests`): the tree build, cycle rejection, axis range, bar percentages, running
extension, slowest id, tick generation and the per-subtree metric sums. The component is the interaction
and rendering layer on top of it.
