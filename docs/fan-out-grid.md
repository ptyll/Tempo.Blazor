# TmFanOutGrid — an orchestrator's fan-out as a grid of work tiles

`TmFanOutGrid` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) shows one job split across N workers: a
summary line above the grid (how much finished, what it cost, when it should be over) and a tile per unit of work with
its worker, its state, its attempt count and a **live** countdown to its estimate.

The shared identity model (`AgentRef`, `AgentActivityStatus`) and the accessibility rules every agent visualization
follows are documented once in [ai-agents.md](ai-agents.md). This page is the `TmFanOutGrid` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmFanOutGrid Tasks="_tasks"
              Title="Reconcile 40 invoices"
              Columns="4"
              @bind-Filter="_filter"
              OnRetry="RetryAsync"
              OnCancel="CancelAsync" />
```

## The models

| Type | What it is |
|---|---|
| `FanOutTask` | One unit of work: `Id`, `Label`, `Status`, `Worker?` (an `AgentRef`), `WorkerId?`, `Attempt` (1-based), `MaxAttempts?`, `StartedAt?`, `EstimatedEndAt?`, `CostMicroUnits?`, `Error?`. A record VALUE a host recomposes with `task with { … }`. |
| `FanOutSummary` | The roll-up the header prints, computed by `FanOutSummary.Create(tasks)`: `Total`, `Queued`, `Running`, `WaitingForApproval`, `Succeeded`, `Failed`, `Cancelled`, `Idle`, `TotalCostMicroUnits`, `HasCost`, `EstimatedEndAt`, plus `Finished`, `Active`, `IsComplete`, `HasFailures`, `Progress` and `RemainingAt(now)`. |
| `FanOutFilter` | Which slice the grid shows: `All` \| `Queued` \| `Running` \| `WaitingForApproval` \| `Succeeded` \| `Failed` \| `Cancelled`. The members mirror `AgentActivityStatus`, so a host never maps between two vocabularies. |

A blank `Id` is generated at construction (the same rule `AgentRef` follows), so a list built in a loop still renders.
`Attempt` below 1 reads as 1 and a `MaxAttempts` of zero or less reads as **no limit**, so a host that never set one can
never block a retry.

### The rules the model carries

The surface and the model agree on four derived facts, so a caller never has to re-implement them:

- **`CanRetry`** — only a FAILED task whose attempts are not used up (`RetryExhausted`). A running task can be
  cancelled, not retried; a succeeded one has nothing to retry.
- **`CanCancel`** — only work that still holds a worker: queued, running or waiting for a decision.
- **`RemainingAt(now)` / `IsOverdueAt(now)`** — the estimate is a **countdown**, never a fact: it is measured against the
  caller's clock, an estimate in the past reads as zero (and paints "Overdue") instead of a negative duration, and a
  finished task has nothing left to count.
- **`CostMicroUnits`** — money is stored in **micro-units** (1 000 000 = 1.00) and formatted by `AiCostFormatter`, the
  same unit and the same formatter the agent-run surfaces use, so one run never mixes two money shapes. `null` is
  "not priced"; a priced zero is a real price.

`FanOutSummary.Create` sums the priced tasks and takes the LATEST estimate among the **active** ones — a finished task
cannot postpone the run — and `HasCost` stays `false` when no task carries a price, so a surface hides the money line
instead of claiming the run was free.

## Parameters

| Parameter | Type | Default | What it does |
|---|---|---|---|
| `Tasks` | `IReadOnlyList<FanOutTask>?` | `null` | The units of work, in the host's order. Null or empty renders the localized empty state. |
| `Title` / `ShowTitle` / `HeadingLevel` | `string?` / `bool` / `int` | `null` / `true` / `3` | The heading; the level is clamped to 2–6. |
| `ShowSummary` | `bool` | `true` | The roll-up line (finished / running / failed, the task count, the cost, the estimate). |
| `Columns` | `int` | `4` | Tiles per row, clamped to 1–8. **The layout authority**: the C# window and the stylesheet read the same number (written inline as `--tm-fan-out-columns`). |
| `Compact` | `bool` | `false` | A tighter tile that keeps the label, the state word and the attempt badge and drops the countdown and the worker line. |
| `ShowFilter` | `bool` | `true` | The state filter bar. |
| `Filter` / `FilterChanged` | `FanOutFilter` | `All` | The state filter, two-way bound (`@bind-Filter`). |
| `VisibleTaskLimit` | `int` | `60` | How many tiles may be mounted at once; `0` mounts the whole filtered list. |
| `OnRetry` / `OnCancel` | `EventCallback<FanOutTask>` | — | The reader asked for a retry / a cancel. The component never changes a task. |
| `CostFormatter` | `Func<long, string>?` | `null` | Formats micro-units; null uses `AiCostFormatter` with `Culture`. |
| `Culture` | `CultureInfo?` | `null` | Formats the costs and the clock times (the countdowns are clock-style `m:ss` on purpose). |
| `TimeProvider` | `TimeProvider?` | `null` | The clock the countdowns and the repaint ticker read; null uses the container's and falls back to `TimeProvider.System`. |
| `Class` / `AdditionalAttributes` | | | The usual root hooks. |

## The header

Done, Running and Failed are counted separately (the three facts a reader scans for), the task count reads
`finished/total`, the cost sums the priced tasks and the estimate is the live time to the LAST active estimate. Once
nothing is active the estimate says "Finished"; a fan-out with no estimates says "No estimate".

## The grid

The grid is an APG **grid**: `role="grid"` with `aria-rowcount` (the filtered total), `aria-colcount` (`Columns`),
a `role="row"` per mounted row carrying its REAL `aria-rowindex`, and a `role="gridcell"` per tile carrying its
`aria-colindex`. Every tile is a **native `<button>`** inside its cell, named by its label first (WCAG 2.5.3), then its
state, its attempt, its countdown, its worker, its cost and its error, joined into one sentence.

| Key | Moves the roving focus to |
|---|---|
| `ArrowRight` / `ArrowLeft` | The next / previous tile in reading order, wrapping at the ends. |
| `ArrowDown` / `ArrowUp` | The tile one ROW below / above, wrapping so the grid is never a dead end. |
| `Home` / `End` | The first / last cell of the CURRENT row. |
| `Ctrl+Home` / `Ctrl+End` | The first / last tile of the whole grid. |
| `PageDown` / `PageUp` | Ten rows down / up. |

The whole grid is ONE tab stop (a roving `tabindex`). Enter and Space are **not** emulated: the tile is a real button,
so the browser activates it exactly once (docs/keyboard-activation-convention.md). Focus is moved
**after** the render (a pending tile id), never inside the key handler, and the per-tile element handles stay alive
across renders so the reference a focus move needs never disappears mid-paint.

### Windowing

Above `VisibleTaskLimit` tiles the grid mounts a **window of whole rows** centred on the focused row, while
`aria-rowcount` keeps declaring the filtered total and every mounted row keeps its real `aria-rowindex`. An sr-only line
reports which slice is mounted, so a screen reader never miscounts the grid. The window follows the focus: `Ctrl+End` on
300 tasks mounts the last rows and focuses the last tile.

### The detail and the actions

The detail follows the tile the reader **focused** (or pointed at), never the mouse alone, so it is reachable without a
pointer. It names the worker, the attempt, the start, the estimated end, the cost and — when it failed — the host's
error verbatim, and it carries the **Retry** and **Cancel** actions, offered exactly while `CanRetry` / `CanCancel`
hold. Both are native buttons, both announce their request through the surface's ONE polite live region, and neither
changes the task: the host owns the data and re-renders. The card's title is a heading ONE level below the surface
heading (capped at h6), so a host that renders the surface at h2 never gets a skipped level.

## Accessibility

- **State is three carriers**: an icon, a localized WORD and a tone. Colour is never the only one (WCAG 1.4.1), and
  `forced-colors` keeps the tone rail as a `CanvasText` border while the word stays visible.
- **ONE polite live region** announces a filter change ("Showing 3 of 18 tasks."), a retry / cancel request and a focus
  that had to move because the tile the reader was on disappeared. Nothing else is announced, and the tiles carry no
  second live region.
- **A filter click keeps the focus on the filter button** — the reader is choosing what to look at, and pulling the
  focus into the grid would make a second filter harder. The roving tab stop does move to the first surviving tile.
- **A host that removes the focused tile** hands the focus to the tile that took the roving stop and says so, instead of
  dropping the reader onto the document body.
- **Reduced motion**: the countdown is TEXT repainted on the injected clock. The partial declares no transition and no
  animation, so `ReducedMotionSelectorTests` has nothing to allowlist and no movement can slip past a reduce block.
- **A coarse pointer** raises every filter chip, action and tile to the 44 px `--tm-touch-target`.
- **Forced colors** hands every focus ring to `Highlight`, keeps the borders `CanvasText` and marks the pressed filter.

## CSS

Classes: `.tm-ai-fan-out`, `--compact`, `__header`, `__title`, `__summary`, `__stats`, `__stat`, `__stat-label`,
`__stat-value`, `__filters`, `__filter(--active)`, `__filter-label`, `__filter-count`, `__empty`, `__grid`, `__row`,
`__cell`, `__tile`, `__tile--{idle|queued|running|approval|succeeded|failed|cancelled}`, `__tile-avatar`,
`__tile-initials`, `__tile-text`, `__tile-label`, `__tile-meta`, `__tile-state`, `__tile-state-icon`,
`__tile-state-text`, `__tile-eta`, `__tile-worker`, `__badge`, `__detail`, `__detail-head`, `__detail-title`,
`__detail-state`, `__detail-state-icon`, `__detail-state-text`, `__detail-facts`, `__detail-error`,
`__detail-actions`, `__action`, `__action--danger`.

Tokens only (no colour literal, no raw palette step, no `[data-theme]` block). The knobs are declared on the root —
`--tm-fan-out-columns` (written inline from `Columns`), `--tm-fan-out-gap`, `--tm-fan-out-tile-min-inline-size`,
`--tm-fan-out-tile-fill`, `-border`, `-tone`, `-avatar`, `-avatar-ink` — and a state modifier SETS
`--tm-fan-out-tile-tone` while the base rule READS it, so source order cannot decide a colour. Every cell is exactly as
wide as every other row's (the same `max(min-inline-size, (100 % − gaps) / columns)` expression), so a partly filled
last row lines up; a narrow container scrolls the GRID, never the page. The worker's avatar colour is written inline
from the shared palette token (`var(--tm-agent-color-{0..7})`), so the eight slots cannot drift from the one palette.

## Wireframe

The palette ships the `fan-out-grid` UI role, the `TmFanOutGrid` schema (`title`, `tasks`, `columns`, `filter`,
`compact`) and a declarative stencil that draws the summary line, the filter chips, one tile per supplied task with its
attempt badge, and the detail card with its Retry / Cancel actions.
