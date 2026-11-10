# TmDelegateBoard — handing work out to agents

`TmDelegateBoard` (package `Tempo.Blazor.AI`, namespace `Tempo.Blazor.Components.AI`) turns a list of tasks and a list
of agents into a board: one **backlog** column and one column per agent, where a task moves by pointer drag, by its own
**Assign** menu, or by an **automatic pass** that hands each unassigned task to the first free agent.

The board renders the snapshot it is given and keeps its own copy of what the reader moved. It never mutates the host's
lists: every committed move is reported once through `OnAssign` or `OnUnassign`, and the host applies it.

```razor
<TmDelegateBoard Tasks="_tasks"
                 Agents="_agents"
                 @bind-AutoAssign="_autoAssign"
                 OnAssign="OnAssign"
                 OnUnassign="OnUnassign" />
```

## The models

Both live in `Tempo.Blazor.Abstractions` (namespace `Tempo.Blazor.AI.Models`) and are **records**: a host recomposes them
with `task with { … }`, and nothing in the library mutates an instance.

| `DelegateTask` | Meaning |
|---|---|
| `Id` | Stable identity the board's overlay is keyed by. **Required** — a blank id throws `ArgumentException`, because an id invented at construction would change on every rebuild and silently move a task back to the backlog. |
| `Title` | What the task is, in the host's words. Blank is allowed and renders the localized "Untitled task". |
| `EstimatedDuration?` | How long it should take. Null, `TimeSpan.Zero` and a negative span are all "not estimated": no duration line, and no projected end time. |
| `Priority?` (`DelegateTaskPriority`) | `Low`, `Normal`, `High`, `Urgent`. **Null means no grade at all** — an invented `Normal` would be a judgement nobody made. |
| `AssignedAgentId?` | The agent holding it, or null for the backlog. A blank value counts as null. |

| `DelegateAgent` | Meaning |
|---|---|
| `Agent` (`AgentRef`) | The shared identity: name, initials, palette slot. Required. |
| `MaxConcurrency` | How many tasks the agent may run at once. Default 1; a value below one is clamped to one. |
| `ActiveCount` | How much work the agent has in flight, as the **host** reports it. |
| `BusyUntil?` | Until when it is busy. A timestamp in the **past** means free now. |

`DelegateMoveRequest` is what a move reports: `Task`, `From?` (null when it came from the backlog), `To?` (null when it
went back to the backlog), plus `AssigneeId`, `IsAssignment`, `IsUnassignment` and `IsHandover`.

### Capacity is the host's, never the board's

`RemainingSlots` is `MaxConcurrency - ActiveCount`, clamped at zero, and `IsFull` is that reaching zero. The board shows
the same numbers in the column header ("1 of 3 running") and **refuses a drop on a full column** — a word plus an icon in
the header, a visible notice line and one announcement. The number of cards a column happens to show never decides
capacity: the host's facts do.

## The columns

The board renders a `role="group"` of columns; the backlog comes first and is the only one with a dashed frame ("this is
the pile you take from"). Every column heading is a real heading one level below the surface title, and every card states:

- its title (or the localized fallback),
- its priority as a **word** plus a flag plus a tone (`data-priority` carries the stable slug),
- its estimate ("Estimated 1 h 30 min"), or nothing at all when there is none.

## Moving a task

**Pointer.** The drag is the shared `tm-sortable.js` over `pointer-drag.js`, driven through `TmSortableList`'s
`Group`/`ListId`/`OnTransfer` support — this component writes no gesture of its own. The columns are *transfer-only*
lists (`AllowLocalReorder="false"`), because the order of cards inside a column means nothing and a keyboard grab that
reorders nothing would announce a change the host ignores. While a card is over another column, that column carries
`tm-sortable-list--drop-target`.

Every column also opts into the module's **live drag-over report** (`TrackDragOver="true"` + `OnDragOver`), so the board
knows WHICH card is being dragged and WHERE it would land *before* the release (see below).

**Keyboard and tap.** Every card carries an **Assign** menu (a `TmContextMenu` whose trigger is the tab stop): one item
per agent, plus "Move back to the backlog" for an assigned card. A full agent is named as full — "Planner (full)" — and
its item is disabled, so the refusal is explained *before* it happens. This menu is the only keyboard path, which is why
nothing is drag-only (WCAG 2.5.7).

**The automatic pass.** `AutoAssign` (two-way, `@bind-AutoAssign`) hands every unassigned task to the first free agent:
the one that becomes free earliest, with a tie keeping the host's declaration order (`DelegateAutoAssigner`). One pass
fills the slots it consumes, so three tasks never all land on the first agent. A task the reader puts **back** in the
backlog stays there until the switch is turned off and on again, so the pass never fights the reader. The pass
announces one summary sentence, not one per task.

## The occupied-until preview

Each agent column prints when it is free: "Free now", "Occupied until 14:20", or — while a card is pointed at and that
card has an estimate — "Occupied until 14:50 with this task". The projection is the agent's own free time plus the task's
estimate (`DelegatePresentation.Project`), and it is honest: with no estimate and a free agent there is nothing to
project, so the column keeps saying "Free now".

The preview follows **focus as well as hover** (a preview that needs a mouse is not available to everybody), and the
column carries `data-preview="true"` plus a `data-state` of `free` / `busy` / `preview` so a host (or a test) never has
to parse the sentence. During a **drag** the target column projects instead (see "The live landing preview" below).

A **leave is deferred** (150 ms, `TmDelegateBoard.PreviewLeaveGrace`): `focusout` bubbles from every card, so stepping from
one card to the next inside the same column fires a leave immediately followed by an enter. Clearing on the leave would
blink the projection off and on, so the clear waits for a grace window that the next enter (or a newer leave) cancels — the
preview is only dropped when the reader really left the column.

## The live landing preview (while a card is being dragged)

The resting preview needs the pointer (or focus) on a card; a **drag** has neither, so the board listens to the module's
throttled drag-over report instead (`SortableDragOverArgs(FromListId, FromIndex, ToListId)`, raised on the source column):

* The first report (`ToListId == FromListId`) tells the board which card left which column.
* A report naming ANOTHER column makes that column the target: it carries `data-drag-over="true"`, its border turns to the
  primary token and its availability line projects the dragged card's estimate — "Occupied until 14:50 with the card being
  moved" (`TmDelegateBoard_DragUntil`) with `data-state="preview"` and `data-estimate="true"`.
* Every OTHER column — the **source** included — falls back to its own resting load, so the source column shows its
  projection **without** the lifted card instead of claiming a time that includes work which is leaving.
* A **full** target is still marked (`data-drag-over="true"` + the danger edge, drawn **dashed** so the refusal is a shape
  and not only a colour) but projects nothing: the board never promises a time for a card it is going to refuse, and the
  column keeps its "Full" word.
* A drag over the **backlog** marks the backlog (`data-drag-over="true"`) and projects no column.
* The gesture's end is a **resting report** (`ToListId == null`), whether the drop was committed, refused, escaped or
  cancelled by `pointercancel` — every column returns to its own state and a focus/hover preview the reader had before the
  drag comes back. Nothing is announced during a drag: the ONE committed move still speaks through the single live region.

`data-drag-over` is the stable hook (a host or a test never parses the sentence); `TmDelegateBoard.IsDragging`,
`DraggedTask` and `IsDragTarget(agent)` are the internal decisions behind it.

## The overlay: the reader's move versus the host's data

The board shows the reader's move **at once** and reports it. It keeps that pick (`task id → agent id`, plus the task
instance it was made against) and drops the entry the moment the host supplies a **new instance** of the same task:

- host re-renders the **same** instances → the reader's move stands (a host that has not applied the move yet cannot
  undo it),
- host supplies a **new** instance (it applied the move, or refused it) → the host's data wins.

A task assigned to an agent the board does not show stays in the backlog, so nothing can disappear.

## Accessibility

- Every state is a **word** plus an icon: capacity, "Full", the priority grade, the availability line.
- ONE polite live region announces every committed move and every refusal; an identical sentence is re-announced through
  an invisible marker (a live region announces a mutation, not a repeat). A live drag speaks through the same region only
  when it ENDS (a committed move or a refusal), never once per hovered column.
- The transfer-only lists render **no** live region of their own, so a board has exactly one.
- A card is a plain `listitem`: the columns are transfer-only, so no `aria-roledescription`, no per-row instructions and
  no tab stop are offered (the column keeps the instructions). The Assign menu is the keyboard path, and its trigger paints
  the `UserPlus` glyph — visibly different from the shared drag handle beside it, so the two controls never look alike.
- An agent column (or the backlog) that is still EMPTY keeps its sortable registration and a one-touch-target hit box, so
  the first card can be dragged into it; the insertion slot over an empty column is `0`.
- The Assign menu's wrapper is the tab stop and carries a real focus ring; on `(pointer: coarse)` it grows to the 44 px
  `--tm-touch-target`.
- `forced-colors: active` hands the borders and the toned chips to `CanvasText`/`Canvas` and every focus ring to a
  `Highlight` outline. The partial declares no transition and no animation.

## CSS

`.tm-ai-delegate` plus its BEM children, with two tone knobs: `--tm-delegate-priority-ink` / `-fill` / `-border` (set by
a `--low` / `--normal` / `--high` / `--urgent` modifier and read by the base rule) and
`--tm-delegate-availability-ink` (set through the `data-state` attribute). Guarded by `AiMarkdownViewCssTests`
(tokens only) and `TmDelegateBoardCssTests`.

## Interaction with the wireframe editor

The `delegate-board` UI role, the `TmDelegateBoard` schema (`title`, `tasks`, `agents`, `auto`, `agentCount`) and a
declarative stencil (the backlog, the agent columns with their capacity line, the full column and the Assign action)
ship in the built-in pack.

**Wireframe check (Fáze 6): no stencil change.** The live landing preview is a runtime STATE of the existing
`delegate-board` role (the `data-drag-over` frame plus the target column's availability line), not a new component, a new
parameter or a new view — a wireframe shows the resting board, exactly as it did before this phase, so
`TmDelegateBoardStencilTests` and the `delegate-board` schema are unchanged.

## Tests

- `DelegateTaskTests` — the models: identity, the estimate and priority rules, capacity, the stale `BusyUntil`, the move
  request.
- `DelegateAutoAssignerTests` — the strategy: candidates, order, the pass, the backlog order, the projection.
- `TmDelegateBoardTests` — bUnit: the columns, the card states, capacity and the full column, the preview, the menu,
  the transfer and the refusal, the live drag-over projection (target, source, full target, backlog, cancel), the
  automatic pass, the overlay, the single live region, localization.
- `TmDelegateBoardCssTests`, `TmDelegateBoardStencilTests`.
