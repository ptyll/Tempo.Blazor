# TmSortableList — a list the reader can reorder

`TmSortableList<TItem>` (namespace `Tempo.Blazor.Components.DataDisplay`) renders rows that the reader can
reorder **by pointer**, **by keyboard** or from a **per-row move menu**. It is the reorder primitive the
library was missing: `TmKanbanBoard` moves cards between columns with HTML5 drag-and-drop, `TmTreeView`
uses HTML5 drag-and-drop for tree nodes, and nothing offered a plain "put these in order" list with a
keyboard path.

The component **never mutates the host's list**. Every committed move raises
`OnReorder(SortableReorderArgs)` with the indexes of the host's own `Items`; the host applies the move and
the next render shows the new order. A host that ignores the callback keeps its order — the row simply
snaps back.

```razor
<TmSortableList Items="@steps"
                KeySelector="@(step => step.Id)"
                ItemLabelSelector="@(step => step.Title)"
                OnReorder="@OnStepReordered"
                AriaLabel="Plan steps">
    <ItemTemplate Context="step">
        <span class="font-medium">@step.Title</span>
    </ItemTemplate>
</TmSortableList>

@code {
    private IReadOnlyList<PlanStep> steps = [ /* ... */ ];

    private void OnStepReordered(SortableReorderArgs args)
    {
        var list = steps.ToList();
        var moved = list[args.FromIndex];
        list.RemoveAt(args.FromIndex);
        list.Insert(args.ToIndex, moved);
        steps = list;
    }
}
```

## API

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Items` | `IReadOnlyList<TItem>?` | `null` | The rows in display order. Null/empty renders `EmptyContent`, or the localized default. |
| `ItemTemplate` | `RenderFragment<TItem>?` | `null` | One row's content; null renders `item.ToString()`. |
| `EmptyContent` | `RenderFragment?` | `null` | Replaces the default "No items to reorder." sentence. |
| `KeySelector` | `Func<TItem, object>?` | `null` | The row's identity — feeds Blazor's `@key` (so a reordered row keeps its DOM node and child component instances) and `data-sortable-key`. Null falls back to the index. Keys must be **unique**: if a selector repeats one, every row falls back to the index (a keyed diff throws on a duplicate sibling key, so the list degrades to an unkeyed list instead of failing). |
| `ItemLabelSelector` | `Func<TItem, string>?` | `null` | The word the live region and the menu use to name a row. Falls back to the item itself when `TItem` is a `string`, then to the localized "Item {n}" with the row's position. The selector's KEY is deliberately not a fallback — a database key is not a name. |
| `OnReorder` | `EventCallback<SortableReorderArgs>` | — | Raised **once per committed move**, never when nothing moved. |
| `Disabled` | `bool` | `false` | Removes every reorder path: no drag, no focus stop, no move menu, no announcements. Rows still render. |
| `HandleMode` | `SortableHandleMode` | `Handle` | `Handle` = only the decorative grip starts a pointer drag; `WholeItem` = the row itself is the drag surface (touch requires a 400 ms hold). |
| `ShowMoveMenu` | `bool` | `true` | The per-row move menu — the tap/mouse alternative to dragging. |
| `AriaLabel` | `string?` | `null` | The list's accessible name; null is the localized "Sortable list". |
| `Class` | `string?` | `null` | Extra root CSS classes. |
| `TrackDragOver` | `bool` | `false` | Opt in to the LIVE drag-over report (below). Default keeps the pre-existing behaviour: the pointer drag reports its ONE commit on release and nothing else. |
| `OnDragOver` | `EventCallback<SortableDragOverArgs>` | — | Raised (throttled) while a row of this list is dragged: the list it would land in, and `ToListId == null` for the resting state when the gesture ends or is cancelled. |

Models: `SortableReorderArgs` (`FromIndex`, `ToIndex`, `Source`), `SortableReorderSource`
(`Pointer` | `Keyboard` | `Menu`), `SortableHandleMode` (`Handle` | `WholeItem`),
`SortableDragOverArgs` (`FromListId`, `FromIndex`, `ToListId`).

`OnSortableReorderCommitted(int fromIndex, int toIndex)` is the `[JSInvokable]` the pointer module calls;
it is public only because JS must reach it, and it runs the same commit path as the keyboard side.

`FocusItemAsync(int index)` moves the keyboard focus to the row at `index` of `Items` — the same index
`OnReorder` reports, not a painted position — and returns whether a focus call was made. A host calls it for
the focus hand-back WCAG 2.4.3 requires when the control a reader was on is deleted with its row (a
`TmFallbackChain` editor does exactly this): the row that took the removed one's place receives the focus
instead of the document body. It returns `false` when there is nothing to focus — the index is outside the
list, the list is `Disabled` (a read-only row is not a tab stop), or the element has not been captured yet.
The reference is kept per row KEY, so a host that applies the move itself and re-renders still focuses the
row it named, not whichever row moved into that position.

## Applying the move

`RemoveAt(FromIndex)` + `Insert(ToIndex, item)` reproduces the gesture exactly, because every intermediate
keyboard step is an adjacent swap **of the same row** — the net effect of "grab, ↓, ↓, drop" is one move
from the start index to the final index. The `Source` tells a host whether to log, announce or undo the
change differently (`Pointer` never fires while the pointer is still moving).

## Keyboard model

The **row** is the focus stop in both handle modes — one tab stop per row, never two. It is a
`role="listitem"` with `tabindex="0"`, `aria-roledescription` = the localized "sortable item", and
`aria-describedby` pointing at the visually hidden instructions. Its accessible name comes from
`aria-labelledby` → the row's content element, so the menu trigger's label never bleeds into it. (A
transfer-only or read-only list renders its rows without the tab stop, the role description and the
per-row description — see [Accessibility](#accessibility).)

| Key | Effect |
|---|---|
| `Enter` (keydown, auto-repeat guarded) | Picks the row up. |
| `Space` (keyup) | Picks the row up; pressed again, drops it. |
| `↑` / `↓` | Moves the grabbed row one position (the rows are painted in the **pending** order). |
| `Home` / `End` | Moves the grabbed row to the first / last position. |
| `Enter` / `Space` | Drops the row (one `OnReorder`). |
| `Escape` | Cancels: the pending order is rolled back and nothing is raised. |
| `Tab` | Cancels the grab (the pending order is rolled back and "Move cancelled" is announced) and then moves the focus out of the row like any other Tab. |
| `Alt` + `↑` / `↓` | Moves the row one position **without** grabbing — an immediate commit. |

Per [keyboard-activation-convention.md](keyboard-activation-convention.md): the row is a non-native
focusable, so `Enter` on keydown and `Space` on keyup are emulated; the decorative handle button is
`tabindex="-1"` + `aria-hidden`, so it is never a second focus stop and never needs emulation.

The page-scroll guard of a grabbed row lives in `tm-sortable.js` (`shouldPreventGrabbedKey`), not in a
render-time `@onkeydown:preventDefault`: the decision needs the KEY, and a render-time bool cannot exclude
`Tab`. While `data-grabbed="true"` on the row, the module calls `preventDefault` for `↑`, `↓`, `Home`,
`End`, `Enter`, `Space` and `Escape` **only when the event target is the row itself** — a key that comes
from a control inside `ItemTemplate` (an input, a link, a button) is that control's and reaches it
untouched (`@onkeydown:stopPropagation` / `@onkeyup:stopPropagation` fence the content in Blazor as well).
`Tab` is never prevented and cancels the grab, so a grabbed row can never trap the focus (WCAG 2.1.2).

## Move menu (the non-drag alternative, WCAG 2.5.7)

Every row renders a menu button (`.tm-sortable-list__move`, an APG menu button over `TmContextMenu` with
the accessible name "Move {row}") offering **Move up**, **Move down**, **Move to top** and **Move to
bottom**. Entries that cannot apply (moving the first row up) are disabled rather than hidden. A menu move
commits immediately. The wrapper around the menu carries `@onkeydown:stopPropagation` /
`@onkeyup:stopPropagation`, so the menu's own `Enter`, arrows and `Escape` never reach the row's grab
handler.

## Pointer path (shared gesture helpers)

The drag is the **shared** `pointer-drag.js` (pointer capture by `pointerId`, a 4 px threshold, the touch
long-press, `Escape` / `pointercancel` / `lostpointercapture` rollback, click swallowing) plus
`createAutoScroller` from `pointer-gesture.js` for the edge scroll. `tm-sortable.js` owns only the
list-specific part:

* `attachSortable(root, dotnet, id, 'handle' | 'whole-item')` delegates from the `role="list"` element, so
  a re-render of the rows never needs a re-attach. Attaching again with the same id replaces the previous
  registration (the registry key is per component instance).
* The dragged row follows the pointer with an inline `transform`, the rows it passes shift by one row
  height + gap, and the landing slot comes from `resolveTargetIndex` — a **pure** function (the row boxes
  and the pointer's Y in, an index out) that carries the whole decision and is node-tested.
* Blazor is told **once**, on release: `OnSortableReorderCommitted(from, to)`. Nothing writes component
  state while the pointer moves, so a 200-row list does not re-render per pointer event.
* Every cancel path restores the inline styles **verbatim** and reports nothing.
* `detachSortable(id)` removes the registration; the component detaches when it turns `Disabled`, and when it is
  **empty and standalone**. An empty list that belongs to a `Group` stays attached: the module builds the drop
  targets of a cross-list drag from its own registry, so a detached empty column could never receive the first
  row. Such a list also keeps a non-zero hit box (`.tm-sortable-list__items--empty` carries `min-block-size:
  var(--tm-touch-target)`), because the module hovers a list by its element rect. A row dropped over an empty
  list of the group is reported at insertion slot `0`.

`HandleMode.Handle` gives the drag a dedicated target with `touch-action: none` (an immediate drag);
`HandleMode.WholeItem` passes `longPressMs: 400` and leaves `touch-action` alone, so a tap and a page scroll
still work. `(pointer: coarse)` grows the handle to the 44 px `--tm-touch-target`.

## Accessibility

* `role="list"` of `role="listitem"` rows; each row is a "sortable item" (localized
  `aria-roledescription`), named by its content and described by the hidden keyboard instructions.
* A list that offers **no in-list move** (`AllowLocalReorder="false"`, or `Disabled`) makes no such promise:
  its rows keep their `role="listitem"`, name and content, but carry no `aria-roledescription`, no per-row
  `aria-describedby` and no tab stop — a screen reader would otherwise announce every task card or run as a
  "sortable item" with no keyboard grab behind it. The `<ul>` keeps the instructions (and, for a transfer-only
  list, the transfer wording) and the host's per-row menu is the keyboard path.
* ONE polite live region for the whole list — never one per row — announces every step of a move:
  picked up, moved to position *p* of *n*, dropped at position *p* of *n*, cancelled, and the boundary
  messages ("already first"); the row is named by `ItemLabelSelector` (or its string text, or the
  localized position label), never by its key. The text is kept sticky (it is never cleared on a re-render, which
  could wipe an announcement before assistive technology read it) and when two consecutive transitions
  would produce the same sentence one invisible marker character is toggled so the region still sees a
  mutation.
* The focus indicator is an **inset** outline (`outline: 2px solid var(--tm-color-primary); outline-offset:
  -2px`): a list inside a scrolling pane would clip an outward ring exactly where it is needed.
  `forced-colors` keeps the system palette in charge (no `forced-color-adjust` opt-out, so the row border
  still follows its hover/disabled state) and re-declares the grabbed and dragged states with `Highlight`.
* Reduced motion: the row transition is enumerated in the `animations.css` reduced-motion block, so the
  rows snap instead of sliding.
* `Disabled` leaves the rows readable but removes the focus stop, the menu, the drag and every
  announcement — a read-only list never invites an action it cannot perform.

## Styling

`tm-sortable-list`, `--disabled`, `--whole-item`, `__items`, `__items--empty` (a grouped list with no rows
keeps a `--tm-touch-target` hit box so it stays a drop target), `__item`, `__item--grabbed` (also set by the
module while dragging), `__handle`, `__content`, `__actions`, `__move`, `__move-trigger`, `__empty` — all
built from `--tm-*` tokens, with no `[data-theme]` block (the dark theme re-points the tokens).

## Wireframe

`TmSortableList` is a wireframe citizen: the `sortable-list` UI role, the schema (`title`, `rows`,
`handleMode`, `showMoveMenu`, `disabled`) and a declarative stencil that draws each of those knobs — the
grabbed row is a raised surface with a primary edge, so the state is not colour alone.

## Moving a row to ANOTHER list (`Group`, `ListId`, `OnTransfer`)

Three optional parameters turn a group of lists into one drag surface, without a second gesture implementation and
without changing what a standalone list does:

```razor
<TmSortableList TItem="Task" Items="Backlog"
                Group="board" ListId="backlog"
                AllowLocalReorder="false"
                OnTransfer="OnTransfer" />

<TmSortableList TItem="Task" Items="PlannerTasks"
                Group="board" ListId="planner"
                AllowLocalReorder="false"
                OnTransfer="OnTransfer" />
```

* **`Group`** — lists that pass the same non-empty value accept each other's rows. The module resolves membership from its
  own registry (never a DOM query), so a group cannot pick up a list rendered by another component instance. While a row
  is over another list, that list carries `tm-sortable-list--drop-target`.
* **`ListId`** — how the host recognises a list inside its group: the value reported as `ToListId` (dropped here) and as
  `FromListId` (one of mine left). Null falls back to the component's instance key.
* **`OnTransfer`** — raised **on the SOURCE list** once per committed cross-list drop, with
  `SortableTransferArgs(FromListId, FromIndex, ToListId, ToIndex, Source)`. `ToIndex` is the insertion slot over the
  target's *current* rows, so a value equal to the row count means "after the last row".
* **`AllowLocalReorder`** — default `true`. A host that only wants transfers sets `false`: the keyboard grab/drop model and
  the per-row move menu are then not rendered (a grab that reorders nothing would announce a change the host ignores) and
  the rows stop being tab stops and stop calling themselves "sortable items", while the pointer drag and its transfers keep
  working. Such a list also renders **no** live region of its own, because it announces nothing by design.
* **Empty columns are drop targets too.** A list that belongs to a `Group` stays attached (and keeps a non-zero hit box)
  while it has no rows, so the first card can be dragged into an empty column, an emptied backlog or an empty run window;
  the insertion slot over an empty target is always `0`.

**Who announces.** The component stays silent on a transfer: only the host knows whether the drop was accepted (a column
at full capacity, a read-only board) and what the move is called. A surface that groups several lists therefore keeps ONE
polite live region for the whole board, and the transfer-only lists contribute none.

**What the host does.** It removes the row from its own source collection, inserts it into the target one and re-renders.
The component never moves an item by itself — the same rule as `OnReorder`.

## Painting the drop BEFORE it happens (`TrackDragOver`, `OnDragOver`)

A board that can say "this card would land *here*" while the pointer is still moving needs to know which list the row is
over — the module owns the gesture, so only it can tell .NET. `TrackDragOver="true"` opts a list into that report:

```razor
<TmSortableList TItem="Card" Items="PlannerCards"
                Group="board" ListId="planner"
                AllowLocalReorder="false"
                TrackDragOver="true"
                OnDragOver="OnDragOver" />
```

* The report is raised **on the SOURCE list** — the one that owns the dragged row — with
  `SortableDragOverArgs(FromListId, FromIndex, ToListId)`. `ToListId` equals `FromListId` while the row is still over its own
  list (that first report is how a host learns *which* row is being dragged), names the target once the pointer crosses into
  another member of the group, and is **`null`** when the gesture ends or is cancelled — the **resting state** a host needs
  in order to take its projection back. A refused drop therefore never leaves a stale preview behind.
* **Throttled and de-duplicated.** The module reports the first change at once (the projection must not lag the pointer), a
  change inside the throttle window (100 ms) once when the window closes — the latest list wins — and an unchanged list
  never. A host that only re-renders on a changed target renders a handful of times per gesture instead of once per frame.
* **A preview, not a move.** Nothing is applied and nothing is announced while the gesture runs: the ONE commit still arrives
  through `OnTransfer` on release, and the host still owns the announcement. The default (`TrackDragOver="false"`) keeps the
  module's behaviour byte-for-byte for every other consumer, so no existing board, plan or kanban had to change.
* A host that clears its projection only on the resting report is correct even for a drop OUTSIDE every list: a pointer that
  leaves every list keeps the last one it was over (the module never invents a target from the gap between two columns), and
  the gesture's end reports the resting state — the report is emitted before the ONE commit invocation, so the host's
  projection is already gone when the move is applied.
* The module's own drop mark (`tm-sortable-list--drop-target`) is unchanged — it names the LIST; a host is free to paint the
  COLUMN (the delegate board does: `data-drag-over`) so its header, its frame and the projection move together.
* Node tests pin the throttle (leading report, trailing report with the latest list, de-duplication, cancel → resting) and the
  opt-in (a list without `dragOver` never reports): `tm-sortable.test.mjs`.

## Testing

* bUnit: `tests/Tempo.Blazor.Tests/DataDisplay/TmSortableListTests.cs` (ARIA, keyboard incl. the `Tab`
  release and the template-content fence, menu, disabled, keying and duplicate keys, the label fallback,
  live region, localization, the cross-list transfer contract and the opt-in drag-over report) and
  `SortableListPageTests.cs` (the `/sortable-list` demo page walk).
* Node: `src/Tempo.Blazor/wwwroot/js/__tests__/tm-sortable.test.mjs` (`npm run test:overlay`).
* Playwright: `tests/Tempo.Blazor.E2E/SortableListE2ETests.cs` — keyboard, pointer drag, the row menu,
  Escape rollback, the inset focus ring, reduced motion, dark theme, a 390 px phone and a scoped axe scan.
