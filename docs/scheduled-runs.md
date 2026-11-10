# TmScheduledRuns — runs placed into a scheduler's windows

`TmScheduledRuns` (package `Tempo.Blazor.AI`, namespace `Tempo.Blazor.Components.AI`) shows a scheduler's **supply**
(`RunWindow` — "Monday 09:00–12:00, capacity 6, already used 1") and the **runs** that compete for it
(`ScheduledRun` — "Nightly export, 6 units"). Where each run lands is *computed*, never drawn by hand: `RunWindowPlanner`
fills the windows in time order, splits a run that does not fit in one window, fills the gaps between runs, and reports the
units that fit nowhere.

```razor
<TmScheduledRuns Windows="_windows"
                 Runs="_runs"
                 @bind-ViewMode="_view"
                 OnMove="OnMove" />
```

## The models

Both live in `Tempo.Blazor.Abstractions` (namespace `Tempo.Blazor.AI.Models`) and are **records**.

| `RunWindow` | Meaning |
|---|---|
| `Id` | Stable identity. **Required** — a blank id throws `ArgumentException` (the plan and the reader's placement are keyed by it). |
| `Start`, `End` | The window's timeline. `End` is **exclusive**, so two adjacent windows partition the day instead of overlapping by an instant. A window that closes before it opens offers no time and is skipped by the planner. |
| `Capacity` | How many units the window accepts. A negative value reads as zero. |
| `Used` | How much the **host** has already committed to work this surface does not show. Taken out of the supply before anything is planned. |

Derived: `EffectiveCapacity`, `EffectiveUsed`, `Available` (never negative), `IsFull`, `IsOverloaded`, `Overflow`,
`Utilization`, `Duration`, `Day`, `Contains(instant)`.

| `ScheduledRun` | Meaning |
|---|---|
| `Id` | Stable identity. **Required**. |
| `Title` | What the run does; blank renders the localized "Untitled run". |
| `EstimatedUnits` | How much capacity it needs. Zero needs nothing; negative reads as zero. |
| `Segments` | The placement the host committed, or an empty list. Its **first** segment's window is the run's preferred start window; the rest is informational. |
| `Status` (`ScheduledRunStatus`) | `Planned` (the default — an unclassified run is planned work, never finished), `Running`, `Succeeded`, `Failed`, `Paused`. |

`ScheduledRunMoveRequest` (`Run`, `From?`, `To`, `WindowId`, `IsChange`) is what a move reports.

## The plan

`RunWindowPlanner.Plan(windows, runs, preferredWindowIds?)` returns one `ScheduledRunPlan` per run, in the host's order:

1. windows are usable when they offer time and capacity, and are taken in their own time order (a tie keeps the host's
   order);
2. a run starts in its preferred window — the reader's move first, then the run's own first segment, then the first window
   with room;
3. it fills what is left of that window, then the windows after it, one **slice** per window; a slice's time is
   proportional to its units, so a three-hour window that accepts four units gives each unit 45 minutes;
4. a run that no longer fits anywhere reports `UnitsUnplaced` — a window is **never** filled past its capacity;
5. units that fit nowhere are named in the table view and in nothing else: the board draws only what it placed.

`PlanOne(windows, run, preferredWindowId)` is the same decision for a single run, and `ScheduledRunPlan` exposes
`IsPlaced`, `IsComplete`, `SpansWindows`, `FirstWindow`, `LastWindow`, `Windows` and `SegmentIn(windowId)`.

## The three views

`ViewMode` (`RunViewMode`, two-way with `@bind-ViewMode`) has one job per value:

- **Week** (the default) — every supplied window, grouped by day. The host decides the span; there is no
  culture-dependent week-start arithmetic anywhere.
- **Day** — one day's windows in full, with `Previous day` / `Next day` buttons that step through the days the windows
  actually cover (`aria-disabled="true"` at the ends, never the native `disabled`, so the focus never drops to `<body>`).
  `SelectedDay` is two-way and defaults to the first day that has windows.
- **Table** — the same schedule as one table: run, window, the time the slice occupies, its units and its status, plus the
  same move menu. This is the **text alternative** (WCAG 1.3.1), not a lesser view.

The reader's pick wins until the host **moves** the `ViewMode` parameter; a re-render with the same value cannot undo it.

## The capacity bar

Each window draws one decorative (`aria-hidden`) bar: the host's committed fill, then one slice per placed run, coloured
from the shared agent palette (`--tm-agent-color-{n}`, by the run's position in the host's list). The bar is painted from
the **same** segments the rows show, so the picture and the numbers cannot disagree, and every number it draws is also a
sentence in the window header:

- "Units used: 1 of 6",
- "Free: 3 units" / "Full",
- "Overloaded by 2 units" with an icon and the danger tone when the host committed **more** than the window accepts.

A run with more than one slice also carries "Part 1 of 2", so a split run is recognisable in every window it touches.

## Moving a run

**Pointer.** The drag is the shared `tm-sortable.js` over `pointer-drag.js`, driven through `TmSortableList`'s
`Group`/`ListId`/`OnTransfer`: each window is a transfer-only list, so nothing announces an order inside a window.
`Alt`+arrow is the keyboard step (below) and the menu is the tap/keyboard alternative.

**Keyboard and tap.** Every run's move menu lists **every** window, with the current one named "(current)" and disabled.
Its trigger paints the `MoreVertical` glyph (`data-icon="more-vertical"`), not a second drag grip: the row already carries
the shared sortable handle, and two identical grips that do different things make a reader grab the wrong one. An empty run
window is still a drop target (its sortable registration is kept while it has no rows) and accepts a run at slot `0`.

### The one-step move chord (`Alt` + arrow)

Beside the menu (which lists every window and therefore never needs to be learned), a focused run row moves one step with
the chord the core `TmSortableList` already uses for its one-step move:

| Chord | What it does |
|---|---|
| `Alt+ArrowLeft` / `Alt+ArrowRight` | Moves the run to the previous / next window in the surface's own time order. |
| `Alt+ArrowUp` / `Alt+ArrowDown` | Moves the run to the FIRST window of the previous / next day. |

* The documented key set is published on the row as `aria-keyshortcuts="Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp
  Alt+ArrowDown"` (board rows and table rows alike) and named in the visible instructions, so a reader can discover it;
  the menu remains the alternative that needs no key knowledge, and the pointer drag remains the third path.
* Two things make the chord impossible to do purely in Blazor: `Alt+ArrowLeft` / `Alt+ArrowRight` is ALSO the browser's
  Back/Forward shortcut (and `@onkeydown:preventDefault` is unconditional per element, so it would swallow `Tab`), and the
  element a reader actually focuses inside a run row is the move menu's trigger, whose content carries
  `@onkeydown:stopPropagation` — Blazor never dispatches that key to the row. The package module
  `_content/Tempo.Blazor.AI/js/tm-scheduled-runs.js` therefore wraps the **shared core helper** `attachKeyboardMove`
  (`_content/Tempo.Blazor/js/pointer-drag.js`) on the surface root: a NATIVE listener there still sees the event (the
  browser bubbles it through the root before Blazor's document-level dispatch). The helper cancels exactly the chord on the
  row that declares `data-tm-run-move-keys`, calls `preventDefault`, never `stopPropagation`; the module resolves that row's
  `data-run-id` and reports ONE `OnRunMoveStep(runId, key)` to the component. No second keyboard implementation, no model in
  the browser. Without JavaScript the chord does nothing — the menu still lists every window, so nothing depends on it.
* Each move goes through the SAME commit as a drop (`MoveToWindowAsync`), so it is announced through the one polite live
  region and a target with no room is REFUSED exactly like a drop (the run stays put, the notice line and the region say
  why). The surface still reports `OnMove` once.
* An end of the range is **announced instead of silently ignored**: "… is already in the first / last window" and "… is
  already on the first / last day". A run the plan did not place has no anchor, so `Alt+ArrowRight` enters at the first
  window and `Alt+ArrowLeft` has nowhere to go.
* `Alt+Shift+Arrow`, `Ctrl+Alt+Arrow` and a plain arrow are left alone — a plain arrow keeps scrolling the page, and the
  chord never steals a key another control owns.

A move is applied to the reader's own overlay, the plan is rebuilt, and the surface then checks what really happened: if
the target window has no room for even the first slice, the move is **refused** — the run stays where it was, and both the
notice line and the live region say why. The surface never reports a placement the plan did not make. Otherwise it
announces "… moved to …" and raises `OnMove`.

The overlay entry is dropped when the host supplies a **new instance** of that run, so a host that re-renders the same
snapshot cannot undo the reader's move and a host that accepted it is never fought.

## Accessibility

- One `role="group"` per presentation, described by the visible keyboard instructions.
- Every state is a word plus an icon (status, full, overloaded); the table is the text alternative.
- ONE polite live region announces a committed move, a refusal and the boundary of the keyboard range; an identical
  sentence is re-announced through an invisible marker.
- The window lists render no live region of their own.
- Run rows are plain `listitem`s of a transfer-only list: no `aria-roledescription`, no per-row instructions and no tab
  stop — the window list keeps the instructions and the move menu is the keyboard path.
- `(pointer: coarse)` grows the day buttons and the move menu to the 44 px `--tm-touch-target`;
  `forced-colors: active` hands the palette to `CanvasText`/`Canvas`/`Highlight`. The partial declares no transition and no
  animation.

## CSS

`.tm-ai-runs`, the `--week` / `--day` / `--table` modifiers, `__board`, `__day`, `__window` (+`--overloaded`, `--full`),
`__bar` / `__bar-committed` / `__bar-slice`, `__run*`, `__status--*`, `__part`, `__move*`, `__table*`, `__notice`.
The runtime knobs the component writes inline (`--tm-runs-slice-start`, `--tm-runs-slice-size`, `--tm-runs-run-color`)
have declared defaults on the root; the inline style always outranks them. Guarded by `AiMarkdownViewCssTests` and
`TmScheduledRunsCssTests`.

## Interaction with the wireframe editor

The `scheduled-runs` UI role, the `TmScheduledRuns` schema (`title`, `runs`, `windows`, `view`, `overloaded`) and a
declarative stencil (the day heading, the window cards with their capacity bar, the free/overloaded line and the run
rows) ship in the built-in pack.

**Wireframe check (Fáze 6): no stencil change.** The move chord is a runtime keyboard path over the existing
`scheduled-runs` role and adds no `[Parameter]`, no view and no element a wireframe would draw, so the schema, the
stencil and `TmScheduledRunsStencilTests` are unchanged (the documented key set lives on the rendered row's
`aria-keyshortcuts`).

## Tests

- `RunWindowTests` — the models: the exclusive end, the clamped capacity, the overload, the preference, the move request.
- Node: `src/Tempo.Blazor.AI/wwwroot/js/__tests__/tm-scheduled-runs.test.mjs` (`npm run test:ai`) — the chord's key/modifier
  filter and the run-id resolution.
- `RunWindowPlannerTests` — the strategy: order, split, gap filling, unplaced units, preferred windows, slice times.
- `TmScheduledRunsTests` — bUnit: the day groups, the capacity and the bar, the split, the views and their switching, the
  table, the move and its refusal, the keyboard chord (both directions, the day step, the range boundaries, a full target,
  the "other modifier does nothing" rule and the table row), the overlay, the single live region, localization.
- `TmScheduledRunsCssTests`, `TmScheduledRunsStencilTests`.
