# TmKanbanBoard — a board whose collapsed zones still catch cards

`TmKanbanBoard<TItem>` (namespace `Tempo.Blazor.Components.DataDisplay`) renders a generic Kanban
board: columns with optional colour and WIP limits, optional horizontal swimlanes, custom card /
column / swimlane templates, cross-column and cross-lane drops, in-column reordering, collapsible
columns and swimlanes, and a non-drag card move for keyboard and touch readers.

The component is a **pure view over the host's list**. Every committed gesture — a drop, a nudge from
the move menu, a keyboard move — reports a `KanbanMoveEvent<TItem>` and the host applies it; the
board never reorders, inserts or removes an item itself. A host that ignores the callback keeps its
order and the next render shows the unchanged board.

```razor
<TmKanbanBoard TItem="TaskItem"
               Items="_tasks"
               Columns="_columns"
               ColumnSelector="@(t => t.Status)"
               CardLabelSelector="@(t => t.Title)"
               ColumnsCollapsible="true"
               @bind-CollapsedColumns="_collapsedColumns"
               OnItemMoved="HandleMove"
               OnItemReordered="HandleMove">
    <CardTemplate Context="task">
        <strong>@task.Title</strong>
        <span>@task.Assignee</span>
    </CardTemplate>
</TmKanbanBoard>
```

The full parameter list, the CSS class table and the knobs live in
[COMPONENTS.md](../COMPONENTS.md#tmkanbanboardtitem); this document is the behaviour contract.

## Layouts

| Layout | When | What renders |
|---|---|---|
| Classic | `SwimlaneSelector` is `null` | One `.tm-kanban__column` per `KanbanColumn`, cards found by `ColumnSelector`. |
| Swimlanes | `SwimlaneSelector` is not `null` | A sticky `.tm-kanban__column-headers` row plus one `.tm-kanban__swimlane` per lane; each lane holds one `(column, lane)` cell per column. |

Lanes come from `Swimlanes` when supplied, otherwise from `SwimlaneSelector` in order of first
appearance. Every card whose selector returns `null` (or an id that `Swimlanes` does not declare)
lands in the trailing **"no value" lane**, titled with the localized `TmKanban_NoSwimlane`. That lane
has an internal id, so it is addressed in `CollapsedLanes` by the public constant
`TmKanbanBoard<TItem>.NoSwimlaneId`; in a `KanbanMoveEvent` it is still reported as
`ToSwimlane = null`, and the sentinel never leaves the component.

## Collapsible columns

`ColumnsCollapsible` (default `false`) puts one native `<button type="button"
class="tm-kanban__column-toggle">` in every column header — classic layout and the swimlane layout's
sticky header row alike — and always **outside** `ColumnHeaderTemplate`, so a host header can never
lose it. The button carries `aria-expanded` and a localized `aria-label`
(`TmKanban_CollapseColumn` / `TmKanban_ExpandColumn` with the column title). It is a real button: no
`role`, no `tabindex`, no Enter/Space emulation, one activation per press.

A collapsed column:

- keeps its `data-testid="board-column-{id}"` element (or `colhead-{id}` in the swimlane layout) and
  gains `tm-kanban__column--collapsed`, so nothing in the DOM disappears;
- renders the title (vertical, `writing-mode: vertical-rl`), the card count and the WIP badge, and
  **no card body at all** — a `VirtualizeCards` column renders no `<Virtualize>` either;
- reports itself as `role="group"` with the plural accessible name
  `TmKanban_CollapsedColumnSummary` ("To Do, 2 cards"), extended with `TmKanban_CollapsedColumnWip`
  and — only when the count exceeds `MaxItems` — `TmKanban_CollapsedColumnOverLimit`;
- survives a WIP breach visibly: the strip states the over-limit state with a solid danger edge and
  danger-text numbers at the non-text 3:1 level, because a strip has no card body left for the
  expanded column's soft tint;
- is a real drop target (see below);
- is expanded by a pointer click anywhere on the collapsed header. That path is deliberately
  pointer-only; the native toggle is the keyboard path and fences its own click, so one gesture never
  toggles twice.

In the swimlane layout a collapsed column narrows the sticky header cell **and** every
`cell-{col}-{lane}` (and every `lane-slot-{col}-{lane}` of a collapsed lane), so the grid stays
aligned. A header-only column keeps its WIP breach visible on its header, which is otherwise
transparent.

State is host-owned when supplied: `CollapsedColumns` / `CollapsedColumnsChanged` and
`CollapsedLanes` / `CollapsedLanesChanged` (`IReadOnlyCollection<string>?`, `null` = the board keeps
its own state) carry the **complete** new set, so `@bind-CollapsedColumns` / `@bind-CollapsedLanes`
round-trip. A host set wins; a toggle only raises the callback on a controlled board and applies the
new set itself on an uncontrolled one. `IsColumnCollapsed` also honours `ColumnsCollapsible`, so a
host-supplied `CollapsedColumns` set is ignored while the feature is switched off.

## Collapsed swimlanes keep one drop slot per column

`SwimlanesCollapsible` (default `true`) renders the lane toggle in the lane header. A collapsed lane
drops its `__swimlane-body` and renders `tm-kanban__swimlane-slots` instead: one
`.tm-kanban__lane-slot` per column (`data-testid="lane-slot-{column}-{lane}"`, the no-value lane
reads `none` exactly like `cell-…-none`). Slot widths copy the grid — the shared
`--tm-kanban-column-width` for an expanded column, `--tm-kanban-collapsed-width` for a collapsed one
(`tm-kanban__lane-slot--collapsed`) — so the column headers stay aligned with the slots below them.

Outside a drag the strip is quiet (a low row with a transparent dashed border). The board root
carries `tm-kanban--dragging` for exactly the length of a gesture; during that time every slot
advertises itself as a target and the slot under the pointer (or the classic collapsed strip) adds
`tm-kanban__lane-slot--drop-target` / `tm-kanban__column--drop-target` — a `--tm-color-primary` pair
that clears the non-text 3:1 requirement in both themes. A `Disabled` board neither highlights nor
accepts a drop.

`--tm-kanban-lane-padding` and `--tm-kanban-lane-inset` drive the lane body padding and the sticky
header row's inset from the same pair, so a header cell and the lane cells it heads share one left
edge (the phase-5 E2E measures real `getBoundingClientRect` boxes for this). `--tm-kanban-column-width`
and `--tm-kanban-collapsed-width` do the same for widths.

## Drop contract

Every drop zone (column body, `(column, lane)` cell, collapsed strip, lane slot) reports the same
event:

- dropping **on a zone** (header, padding, empty area, a collapsed strip or a collapsed lane's slot)
  appends: `TargetIndex` = the cell's card count, `TargetBeforeItem` = `null`;
- dropping **on a card** resolves the insertion index from ordering only (no JS measurement): dragging
  a card downward past its current position drops it after the hovered card, otherwise before it;
- a drop in the **same cell** raises `OnItemReordered`; a drop in a different column or lane raises
  `OnItemMoved`. Without an `OnItemReordered` handler a same-cell drop is a no-op, matching the legacy
  behaviour;
- `FromSwimlane` / `ToSwimlane` are only populated when swimlanes are enabled and are `null` for the
  no-value lane;
- the horizontal `.tm-kanban__drop-indicator` line is drawn only by an expanded column; a narrowed
  zone has no room for it and reports itself with its outline instead.

Prefer `TargetBeforeItem` when persisting order: it is the item the card should be inserted before and
is unaffected by whether the dragged item is removed before or after the insertion point is computed.
`TargetIndex` is the zero-based index in the cell's current ordering; a value equal to the card count
means "append".

The mouse's HTML5 drag announces nothing — the live region speaks for the touch path (its drop and
its cancel) and for the non-drag move below; one board, one polite live region either way.

## Column colours — tokens, not hexes

`KanbanColumn.Color` (and `KanbanSwimlane.Color`) render **verbatim** as the inline
`background-color` of `.tm-kanban__header-color` / `.tm-kanban__swimlane-color`; the component
invents no default colour, so a column without one renders no dot. The recommendation (the TG-005
mock gap): pass a **token reference** instead of a raw hex, so the dot follows the theme in light
AND dark:

```csharp
new KanbanColumn("todo", "To Do", "var(--tm-kanban-column-todo)")
```

The catalogued `--tm-kanban-column-backlog` / `-todo` / `-doing` / `-review` / `-done` tokens ship in
`tokens.css` and lift to the 400-tones on the dark roots (`tokens-dark.css`) — the same convention
as the TG-002 chart series palette. The dot is *identifying* paint (WCAG 1.4.11), so every value
clears the non-text **3:1** level against the header's muted panel in both themes; the plain
500-tones a host would reach for first measure only 1.9–2.3:1 there, which is why the light values
are the *strong* tones (`--tm-color-info-strong`, `…-warning-strong`, `…-success-strong`,
`primary-600`). `TmKanbanColumnTokenTests` computes the contrast for all ten values. A raw hex (or
any CSS colour) still works — the parameter keeps its meaning for hosts with a brand palette of
their own.

The colour stays visible while the zone is collapsed: the strip keeps its header dot, and a
collapsed lane's slot renders the column's mark as `.tm-kanban__slot-color` (a quiet bar at the
slot's start edge, a horizontal cap under a collapsed column) — without it a collapsed lane would
hide which column each slot belongs to. Every slot also names its target in `title` /
`aria-label` as a phrase — `TmKanban_LaneSlotTarget`, "{0} in lane {1}" ("To Do in lane Alice") —
and carries `role="group"` like the collapsed column strip, so a reader dropping onto a collapsed
zone hears where the card will land.

## Touch drag (decision O7, plan 92e96b25)

On a touch pointer the board drags cards through the **shared** `pointer-drag.js` helper (via the
`tm-kanban.js` module) — never a second drag implementation:

- A **long-press starts the drag** (400 ms). Before the hold completes the card keeps its native
  behaviour — a tap still clicks, a move is a page scroll (drifting past the tolerance aborts the
  gesture as a swipe); after it, the card lifts and follows the finger.
- The module paints everything with a **ghost**: the source card stays in the flow invisibly (its
  siblings never jump, and the column's `overflow` can never clip the drag — a translated card
  would vanish the moment it left its column), while a fixed-position clone rides the finger
  (`.tm-kanban__card--touch-dragging`). The clone is **inert and anonymous** — `inert`, and its
  subtree's `id` / `data-testid` stripped — so no test, focus or assistive technology can address
  it, and it keeps riding the finger until the commit report settles on the .NET side (with a
  timeout), so on Blazor Server the card never visibly snaps back for the length of the round
  trip. The hovered zone carries its `--drop-target` / `--touch-target` class and the insertion
  line (`.tm-kanban__touch-indicator`) sits between the other cards. **Blazor never re-renders
  mid-gesture**, which is what keeps the captured card's DOM node alive for the whole drag; the
  insertion index resolves by the midpoint rule against the OTHER cards of the hovered cell.
- The drop report carries **identities, not guesses**: the source is the press-time
  `(column, lane, index)` triple PLUS the card's **stable key** (`data-tm-kanban-key`, the card's
  index in the host's full `Items` list) — the board verifies at release that the key still
  addresses the same card and **cancels on mismatch** (a host refresh under the gesture moves
  nothing, and the rollback is announced). The target is the `(column, lane)` pair with the
  **full-list index of the card before which the drop lands** (`-1` = append): the module reads the
  hovered card's own `data-tm-kanban-index`, so a `VirtualizeCards` cell — whose DOM holds only
  the visible window — reads exactly the slot a fully-rendered cell would.
- **Edge auto-scroll** rides the shared `pointer-gesture.js` `createAutoScroller` (the
  `tm-sortable.js` / `gantt.js` composition): the board root scrolls horizontally and the hovered
  cell's `.tm-kanban__cards` list scrolls vertically (the scroller swaps on every zone change),
  and the zone/indicator hit test re-runs on every scrolled frame — the content moves under a
  stationary finger.
- **HTML5 dragstart stays the mouse path**: the module blocks a native `dragstart` in the capture
  phase whenever the last press was a touch or a touch session owns the gesture, and on
  `(pointer: coarse)` the card disables the touch callout and text selection for the length of the
  hold.
- The **one commit** arrives on release and reports the same `KanbanMoveEvent` contract as an HTML5
  drop (the board resolves the item from the host's unchanged list by the triple the module
  reports). A release over no zone rolls back and announces the cancel.
- **pointercancel / Escape / a second finger** (all owned by the shared helper) roll back every
  inline paint and are **announced** through the one polite live region
  (`TmKanban_DragCancelled`, "Move of {0} cancelled — the card stayed in {1}").
- **Collapsed zones are full citizens of the touch path**: the strip and every lane slot carry the
  module's `data-tm-kanban-*` identity and highlight under the finger exactly like the HTML5 path.
- The mouse keeps the HTML5 drag-and-drop path untouched, and the per-card "Move…" menu stays the
  WCAG 2.5.7 alternative for every pointer.

The module's pure resolvers and the gesture lifecycle are node-tested (`npm run test:overlay`,
`__tests__/tm-kanban.test.mjs`); the .NET commit/cancel contract is bUnit-tested
(`TmKanbanTouchDragTests`); the browser path is E2E-tested with `hasTouch` + CDP
`Input.dispatchTouchEvent` (`KanbanTouchDragE2ETests`).

## Non-drag move (WCAG 2.5.7 / `ShowMoveMenu`)

A drag-only card fails WCAG 2.5.7, so the accessible path is on by default:

- Every card is focusable with **one roving tab stop per cell** (`Tab` reaches the cell's card, `Tab`
  again leaves it for the next cell). `CardTemplate` content is fenced on keydown **and** keyup, so a
  link, input or button a host renders inside a card keeps its own keys and its own activation.
- The card is `role="group"` with the localized `aria-roledescription` and an `aria-describedby`
  pointing at the board's visually hidden instructions (the "Enter to open a card" sentence only
  appears when the host actually bound `OnCardClick`).
- `Enter` (keydown, auto-repeat guarded) and `Space` (keyup) activate `OnCardClick` — exactly where a
  native button would fire. The card is non-native, which is why it emulates them
  ([keyboard-activation-convention.md](keyboard-activation-convention.md)).
- `M` / `Alt+ArrowDown` on the focused card opens the card's "Move…" menu; the arrow keys (and
  `Home` / `End`) walk the cards of ONE cell — leaving the cell is a `Tab`. A Ctrl/⌘ chord is
  ignored — it belongs to the browser.
- The visible trigger in the card's reserved top-right corner (`tm-kanban__card-move-trigger`, 24×24,
  grows to `--tm-touch-target` on a coarse pointer) opens the same `TmContextMenu`. Its
  `TriggerTabIndex` is `-1` because the card already owns the cell's single tab stop, and
  `MobilePresentation="Auto"` presents the menu as the shared bottom sheet on a phone.
- The menu lists **Move up / Move down** for the card's own cell (disabled at either end of the
  cell) and one target per column — per column × swimlane when swimlanes are on, collapsed columns
  and collapsed lanes included. The cell the card already sits in is named "To Do (current)" and
  rendered `disabled` with `aria-current="true"` instead of offering a no-op target.
- Every choice reports the **same contract as a drop**: a target appends to the chosen cell
  (`OnItemMoved`), a nudge reports `OnItemReordered` at the position the card ends up before. The
  board announces the outcome once through one polite live region
  (`TmKanban_CardMoved`, "{0} moved to {1}", the target read as "column" or "column / lane") and hands
  the focus back to the moved card — or, when the destination is a collapsed strip or a collapsed
  lane's slot, to that zone. The hand-back runs one render batch later than the move so the menu's own
  focus restore cannot undo it.
- `CardLabelSelector` names the card in the trigger, the sheet title, the "(current)" entry and the
  announcement. Without it the board uses the item's `ToString()` only when the type overrides it (a
  record's generated `ToString()` names its fields); a plain class or struct would print its type name,
  so the board falls back to the localized "card {n}" with the card's 1-based position in its cell.
- `Disabled` hides the action entirely (no tab stop, no menu, no live region) and leaves the drag
  markup inert. `ShowMoveMenu="false"` restores the pre-2.11 card markup exactly — the migration note
  is in [CHANGELOG.md](../CHANGELOG.md) (2.11.0).

## Accessibility

- Every state is carried by more than colour: a WIP breach has the danger edge **and** the
  `TmKanban_CollapsedColumnOverLimit` word in the strip's accessible name; a target zone has an
  outline plus its own wash; the chevron rotates on `aria-expanded`, the attribute assistive
  technology announces.
- Focus is never lost: the collapse toggles and cards carry the shared `--tm-focus-ring`, and a move
  hands the focus back (WCAG 2.4.3).
- Toggles are real buttons with a 24 px glyph that grows to `--tm-touch-target` on a coarse pointer;
  the card trigger does the same.
- `prefers-reduced-motion` suppresses the card, indicator, slot and chevron transitions.
- The board keeps exactly one polite live region and one hidden instructions paragraph; both exist
  only while the non-drag path does.

## Test ids

All ids go through `TmComponentBase.TestId`, so `TestIdPrefix="board-a"` namespaces every one of them
and two boards on one page never collide.

| Id | Element |
|---|---|
| `kanban-status` | The polite live region |
| `board-column-{col}` | Classic column |
| `colhead-{col}` | Header cell in the swimlane layout |
| `column-toggle-{col}` | Column collapse toggle |
| `cards-virtual-{col}` | Virtualized card body |
| `cell-{col}-{lane}` | `(column, lane)` cell |
| `lane-slot-{col}-{lane}` | Drop slot of a collapsed lane |
| `swimlane-{lane}` | Swimlane |
| `card-{col}-{lane}-{index}` | Card |
| `card-move-{col}-{lane}-{index}` | Card "Move…" trigger |
| `move-up` / `move-down` | Nudge entries of the move menu |
| `move-to-{col}` / `move-to-{col}-{lane}` | Target entries of the move menu |

`{lane}` is `none` for the no-value lane (the same token `cell-…-none` uses), and the swimlane test id
reads `swimlane-none` for it.

## Localization keys

| Key | Meaning |
|---|---|
| `TmKanban_CollapseColumn` / `TmKanban_ExpandColumn` | `{0}` = column title |
| `TmKanban_CollapsedColumnSummary` | Plural family: `{0}` = count, `{1}` = column title |
| `TmKanban_CollapsedColumnWip` | `{0}` = the WIP limit |
| `TmKanban_CollapsedColumnOverLimit` | "over limit" |
| `TmKanban_ToggleSwimlane` | Swimlane toggle label |
| `TmKanban_NoSwimlane` | The no-value lane title |
| `TmKanban_CardMoved` | Announcement, `{0}` = card, `{1}` = target |
| `TmKanban_MoveCard` / `TmKanban_MoveUp` / `TmKanban_MoveDown` / `TmKanban_CurrentTarget` | Move menu |
| `TmKanban_LaneSlotTarget` | Collapsed-lane slot `title`/`aria-label` phrase: `{0}` = column, `{1}` = lane |
| `TmKanban_DragCancelled` | Touch-drag cancel announcement: `{0}` = card, `{1}` = the cell it stayed in |
| `TmKanban_CardRoleDescription` / `TmKanban_CardNumber` / `TmKanban_CardInstructions` / `TmKanban_CardInstructionsOpen` | Card a11y |
| `TmKanbanBoard_Empty` | Empty cell |

## Wireframe citizen

The board is a wireframe citizen: the `kanban-board` UI role in `BuiltInUiRoleVocabularySource`
(with the synonyms `kanban` / "kanban tabule"), the `TmKanbanBoard` schema in
`BuiltInComponentSchemas` (`columns`, `showAddCard`) and the native renderer
`TempoNativeRendererDefinitions.RenderKanbanBoard`, which draws the columns and their cards into the
wireframe SVG. The collapsible column is a **view state of that same role**, not a new UI role, so the
role vocabulary and the schema stay unchanged; a wireframe mock represents it by drawing one narrow
column, and a host that needs it does so in the wireframe document itself.

## Demo, tests and verification

- Demo pages: `/kanban` (both boards turn `ColumnsCollapsible` on; the "Last change" line reports
  `ToColumn`, `ToSwimlane`, `TargetIndex` and `TargetBeforeItem`), `/data-display` for the
  component-level reference.
- bUnit: `TmKanbanBoardTests`, `TmKanbanBoardCollapseTests`, `TmKanbanBoardCollapseLocalizationTests`,
  `TmKanbanBoardMoveTests`, `TmKanbanCollapseCssTests`, `TmKanbanTouchDragTests` (the touch commit /
  cancel contract and the module's DOM selectors), `TmKanbanColumnColorTests` +
  `TmKanbanColumnTokenTests` (the colour contract and the 3:1 token guard), plus
  `TmResourceCompletenessTests`.
- E2E: `KanbanReorderE2ETests`, `KanbanSwimlaneE2ETests`, `KanbanCollapseE2ETests`,
  `KanbanDropZonesE2ETests`, `KanbanCollapsedLayoutE2ETests`, `KanbanMoveE2ETests`,
  `KanbanTouchDragE2ETests` (real touch input via CDP: long-press drag to an expanded column, a
  collapsed strip and a collapsed lane's slot; a virtualized column scrolled before the drop
  anchors on the full-list index; the board edge auto-scrolls near the edge and stays still away
  from it; pointercancel / a second finger roll back and are announced), `KanbanCollapsedDropE2ETests` (edge cases: collapsed column in a collapsed lane, the
  "no value" slot, an over-limit strip, a Disabled board, the virtualized board, two boards with
  `TestIdPrefix` on one page; screenshots in
  `tests/Tempo.Blazor.E2E/TestResults/kanban-collapsed-drop/`), `KanbanVisualE2ETests` (committed
  light/dark baselines under `tests/Tempo.Blazor.E2E/__screenshots__/kanban/`).

```bash
dotnet test tests/Tempo.Blazor.Tests --filter "FullyQualifiedName~Kanban"
dotnet test tests/Tempo.Blazor.E2E --filter "FullyQualifiedName~Kanban"   # needs the https demos
```
