# TmFacetFilter

`TmFacetFilter` narrows a list through **grouped options with result counts** — the filter menu of a
catalogue, a search result page or a data table. It renders labelled groups of checkboxes, an
optional search box inside a long group, a "Show {n} more" truncation, the active filters as
removable chips, a clear-all action and a result-count confirmation.

```razor
<TmFacetFilter Groups="groups" @bind-Selected="selected" ResultCount="resultCount" OnApply="Load" />

@code {
    private IReadOnlyDictionary<string, IReadOnlySet<string>>? selected;
    private int? resultCount;

    private readonly FacetGroup[] groups =
    [
        new()
        {
            Key = "category",
            Label = "Category",
            Options =
            [
                new() { Value = "electronics", Label = "Electronics", Count = 512 },
                new() { Value = "books", Label = "Books", Count = 301 },
                new() { Value = "garden", Label = "Garden", Count = 188, IsDisabled = true }
            ]
        },
        new() { Key = "status", Label = "Status", SearchThreshold = 0, Options = [ /* ... */ ] }
    ];
}
```

## The model

`FacetGroup` and `FacetOption` live in `Tempo.Blazor.Abstractions` (`Tempo.Blazor.Models`), so a
backend can produce them and a host can render them without a Blazor dependency.

| `FacetGroup` member | Type | Meaning |
| --- | --- | --- |
| `Key` | `string` | The dictionary key of the selection this group owns. Matched ORDINALLY. A blank key skips the group — its options could not be reported back. |
| `Label` | `string` | The disclosure's name and the prefix of every chip this group produces. A blank label falls back to `Key`. |
| `Options` | `IReadOnlyList<FacetOption>` | Rendered in the host's order; the component never re-sorts (facet counts have their own order). |
| `IsCollapsed` | `bool` (default `false`) | The **initial** state only. The reader owns the disclosure afterwards. |
| `MaxVisible` | `int?` | Rows before "Show {n} more". Null uses the component's `MaxVisible`; `0` or less shows everything. |
| `SearchThreshold` | `int?` | Options that turn the group's search box on. Null uses the component's `SearchThreshold`; `0` or less disables it for this group. |

| `FacetOption` member | Type | Meaning |
| --- | --- | --- |
| `Value` | `string` | The programmatic value returned in the group's set. |
| `Label` | `string` | The visible and accessible name; falls back to `Value`. |
| `Count` | `int?` | Results this option leaves — visible next to the label **and** inside the checkbox's accessible name. Null renders no count. |
| `Icon` | `string?` | Optional decorative icon (`aria-hidden`). |
| `IsDisabled` | `bool` | The option cannot be toggled and keeps its count visible. An option that is already selected stays selected and keeps its chip. |

Both types are records, so their generated equality is the record's: `FacetGroup.Options` is an
`IReadOnlyList<FacetOption>` and therefore compares by **reference**. Two groups built separately
from equal options are not equal by value — use `Key` to identify a group (the component does), and
do not rely on `==` for change detection.

## The selection

`@bind-Selected` is an `IReadOnlyDictionary<string, IReadOnlySet<string>>` — group key → selected
values. Every gesture raises **one immutable snapshot** through `SelectedChanged`: a fresh
dictionary of fresh sets, with a group that has nothing selected omitted rather than shipping an
empty set. A host that keeps the previous value never sees it mutate.

`Selected` is an **input**, not a shared mutable state:

- the host supplies a new value → it is adopted (a bound `@bind-Selected` feeds the snapshot back,
  which is detected as equal and ignored);
- the host re-supplies the same reference (or nothing at all) → whatever the reader picked stays,
  so a re-render never silently clears the filter.

**Forcing a reset.** Because a re-supplied reference is indistinguishable from an ordinary re-render,
a host that *refuses* a change — a validation guard, or a round-trip that kept the old filter — must
pass a **new** dictionary instance holding the selection it wants rendered. Mutating the dictionary it
already passed and re-supplying it changes nothing on screen.

| Host behaviour | What the filter renders |
| --- | --- |
| passes a new `Selected` value | the new selection (a reset/revert) |
| re-supplies the same reference | the reader's live selection |
| binds `SelectedChanged` back through `@bind-Selected` | the snapshot it just produced (equal, so no flicker) |

A key the component does not know is preserved untouched and rendered as a removable chip labelled
with the raw value — a filter applied by a URL is never lost just because the current group list
does not carry it.

## Presentations

| `Presentation` | Wide layout | Phone layout |
| --- | --- | --- |
| `Popover` (default) | a trigger button plus an anchored **dialog popover** (`TmOverlayPanel`, `Role="dialog"`, focus trapped) | a trigger button plus the core **bottom sheet** (`TmDrawer`, `Position=Bottom`) |
| `Inline` | the groups in the page flow, with a heading | the same inline groups — there is nothing to open |

The phone band is measured from the viewport scope (`TmLayoutObserver IsViewportScope="true"`); a
host without one still gets the desktop popover, and `LayoutMode="TmLayoutMode.Mobile"` pins the
sheet for a test or a fixed-narrow surface.

Both surfaces carry the same `FacetBody` (groups, search, truncation) and the same footer, so the
three presentations cannot drift.

## Applying and confirming

Filters apply on **every toggle** — the values are the state. `ResultCount` is what adds a
confirmation step: the footer's primary action becomes "Show {n} results" (the grammatical form of
the count comes from `Loc.Plural`, so English says "Show 1 result" and Czech "Zobrazit 5 výsledků")
and confirming closes the surface and raises `OnApply`. Without `ResultCount` the action reads the
neutral "Show results" and nothing is announced.

## Counts, search and truncation

- A count is formatted with `Culture` (default: the ambient culture) as `N0`, so `2100` reads
  "2,100" in English and "2 100" in Czech.
- The checkbox's accessible name carries the count (`"Electronics (512)"`), so the number is never a
  visual-only fact.
- The group's search box appears at `options.Count >= SearchThreshold` and matches the label
  accent-insensitively (`usti` finds "Ústí"), like the other filterable components.
- "Show {n} more" reveals the rest of the group; `n` is the localized plural form.

## Accessibility

- Native `<button>` disclosures and native `<input type="checkbox">` options — no key emulation, so
  Tab, Space and the checkbox's own announced state are the browser's.
- The trigger carries `aria-haspopup="dialog"`, `aria-expanded` and (only while open) `aria-controls`.
  The popover is a `dialog` named by its visible heading and traps focus; Escape closes it and
  overlay.js returns focus to the trigger, exactly as it does for the other dialog popovers.
- A disclosure's selected count is part of its accessible name (`"Status (selected: 2)"`), so a
  screen reader hears the state instead of a bare number.
- **Focus never falls to the page body.** Three gestures remove the control that held focus, so each
  one hands focus on after the render that removed it (WCAG 2.4.3 Focus Order):
  - **removing a chip** → the remove button of the chip that takes its place (the previous one when
    the removed chip was last); when the row empties, the trigger;
  - **the page-level clear-all** (the whole chip row disappears) → the trigger;
  - **the footer clear-all** (its own button disables itself) → the confirmation action of the still
    open surface;
  - **"Show {n} more"** (the button removes itself) → the checkbox of the first option it revealed.
  An inline filter has no trigger, so the fallback there is the first group's disclosure (the heading,
  which carries `tabindex="-1"`, when there are no groups). The hand-off is armed by the gesture only
  and applied in `OnAfterRenderAsync`, so a parameter-driven re-render never moves focus.
- **One** polite live region announces the new result count when the host changes `ResultCount`.
  Nothing else is announced: a checkbox's state change and a disclosure's count are already spoken
  by the browser, and repeating them here would be a duplicate announcement.
- `(pointer: coarse)` grows the option row, the disclosures, the "show more" and the footer actions
  to the 44 px target.
- Under `forced-colors: active` every focusable control gets its own `Highlight` outline (a
  box-shadow focus ring is forced to none there) and the count badge keeps its paint with a system
  edge.
- The only motion is the chevron rotation, listed in the reduced-motion block of `animations.css`.

## CSS

`_facet-filter.css` ships with the core bundle (`tempo-blazor.css` / `tempo-blazor.bundled.css`).
Every `.tm-facet-filter*` class in the component's markup has a rule there; colours, spacing and
radii are `--tm-*` tokens, so the dark theme re-points the tokens and there is no `[data-theme]`
block in the file. The panel's surface is painted by the owning class because the
`:where()` reset in `_overlay-panel.css` has zero specificity.

## Not a re-implementation of `TmFilterBuilder`

`TmFilterBuilder` builds **conditions** ("Amount greater than 100") over typed fields.
`TmFacetFilter` narrows a result set through **known values with counts**. They compose: a page can
own both and merge their state into one query.
