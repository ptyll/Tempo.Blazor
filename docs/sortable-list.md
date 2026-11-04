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

Models: `SortableReorderArgs` (`FromIndex`, `ToIndex`, `Source`), `SortableReorderSource`
(`Pointer` | `Keyboard` | `Menu`), `SortableHandleMode` (`Handle` | `WholeItem`).

`OnSortableReorderCommitted(int fromIndex, int toIndex)` is the `[JSInvokable]` the pointer module calls;
it is public only because JS must reach it, and it runs the same commit path as the keyboard side.

## Applying the move

`RemoveAt(FromIndex)` + `Insert(ToIndex, item)` reproduces the gesture exactly, because every intermediate
keyboard step is an adjacent swap **of the same row** — the net effect of "grab, ↓, ↓, drop" is one move
from the start index to the final index. The `Source` tells a host whether to log, announce or undo the
change differently (`Pointer` never fires while the pointer is still moving).

## Keyboard model

The **row** is the focus stop in both handle modes — one tab stop per row, never two. It is a
`role="listitem"` with `tabindex="0"`, `aria-roledescription` = the localized "sortable item", and
`aria-describedby` pointing at the visually hidden instructions. Its accessible name comes from
`aria-labelledby` → the row's content element, so the menu trigger's label never bleeds into it.

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
* `detachSortable(id)` removes the registration; the component detaches when it turns `Disabled` or empty.

`HandleMode.Handle` gives the drag a dedicated target with `touch-action: none` (an immediate drag);
`HandleMode.WholeItem` passes `longPressMs: 400` and leaves `touch-action` alone, so a tap and a page scroll
still work. `(pointer: coarse)` grows the handle to the 44 px `--tm-touch-target`.

## Accessibility

* `role="list"` of `role="listitem"` rows; each row is a "sortable item" (localized
  `aria-roledescription`), named by its content and described by the hidden keyboard instructions.
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

`tm-sortable-list`, `--disabled`, `--whole-item`, `__items`, `__item`, `__item--grabbed` (also set by the
module while dragging), `__handle`, `__content`, `__actions`, `__move`, `__move-trigger`, `__empty` — all
built from `--tm-*` tokens, with no `[data-theme]` block (the dark theme re-points the tokens).

## Wireframe

`TmSortableList` is a wireframe citizen: the `sortable-list` UI role, the schema (`title`, `rows`,
`handleMode`, `showMoveMenu`, `disabled`) and a declarative stencil that draws each of those knobs — the
grabbed row is a raised surface with a primary edge, so the state is not colour alone.

## Testing

* bUnit: `tests/Tempo.Blazor.Tests/DataDisplay/TmSortableListTests.cs` (ARIA, keyboard incl. the `Tab`
  release and the template-content fence, menu, disabled, keying and duplicate keys, the label fallback,
  live region, localization) and `SortableListPageTests.cs` (the `/sortable-list` demo page walk).
* Node: `src/Tempo.Blazor/wwwroot/js/__tests__/tm-sortable.test.mjs` (`npm run test:overlay`).
* Playwright: `tests/Tempo.Blazor.E2E/SortableListE2ETests.cs` — keyboard, pointer drag, the row menu,
  Escape rollback, the inset focus ring, reduced motion, dark theme, a 390 px phone and a scoped axe scan.
