# Toolbar: priorities, overflow and labels

`TmToolbar` holds `TmToolbarButton`s, `TmToolbarDivider`s and any custom content. Since F4 it can
move the buttons that do not fit into a **More** menu instead of overflowing or scrolling, and
buttons can show their text next to or under the icon.

```razor
<TmToolbar Overflow="ToolbarOverflow.Menu" Labels="ToolbarLabels.IconsWithText" AriaLabel="Diagram tools">
    <TmToolbarButton Icon="@IconNames.Plus" Text="New" LabelPosition="ToolbarLabelPosition.Below" />
    <TmToolbarButton Icon="@IconNames.Copy" Text="Copy" Priority="ToolbarButtonPriority.Secondary" />
    <TmToolbarDivider />
    <TmToolbarButton Icon="@IconNames.Trash" Text="Delete" Variant="ButtonVariant.Danger"
                     Priority="ToolbarButtonPriority.OverflowOnly" />
    <TmToolbarButton Icon="@IconNames.Check" Text="Save" Variant="ButtonVariant.Primary"
                     Priority="ToolbarButtonPriority.Pinned" />
</TmToolbar>
```

## Parameters

| Component | Parameter | Values | Default |
|-----------|-----------|--------|---------|
| `TmToolbar` | `Overflow` | `None` · `Menu` | `None` (nothing changes for existing toolbars) |
| `TmToolbar` | `Labels` | `Auto` · `Icons` · `IconsWithText` | `Auto` |
| `TmToolbar` | `OverflowLabel` | accessible name + tooltip of the More trigger | localized "More" |
| `TmToolbar` | `AriaLabel` | accessible name of the toolbar | `Title`, then a localized "Toolbar" |
| `TmToolbar` | `OverflowPresentation` | `PanelPresentation` of the More menu | `Auto` (sheet on phones) |
| `TmToolbarButton` | `Priority` | `Primary` · `Secondary` · `OverflowOnly` · `Pinned` | `Primary` |
| `TmToolbarButton` | `LabelPosition` | `Inline` · `Below` · `Hidden` | `Inline` |

## Overflow = Menu

* **Priority is an overflow rank, not a rank number and not an order.** Buttons render in the order you
  write them, always — the menu too. `Secondary` leaves the bar before any `Primary`; of two buttons
  with the same priority the *later* one (in DOM order) leaves first. `OverflowOnly` buttons never
  appear on the bar and never count against the room. `Pinned` is the opposite pin: the button
  **never** moves into More at any width (the trailing Save / primary call to action); it is measured
  as fixed width, like a title, and is not counted against the room the collapsible buttons share. The
  enum values are *not* ranks — `Pinned` is the numerically last member (it was added after
  `OverflowOnly`, so existing values keep their numbers): never compare or cast them; the toolbar maps
  them through `ToolbarOverflowLayout.Rank`. If nothing collapsible fits, the shared partition keeps
  one button on the bar (the clamp) — a pinned button satisfies it by itself.
* **One ordering rule.** Which buttons leave is `ActionOverflowLayout.Partition` — the same
  function the F5 `TmMobileActionBar` uses (through `ToolbarOverflowLayout`, which maps
  `Priority` to the rank, `OverflowOnly` to the `Always` pin and `Pinned` to the `Never` pin). The
  toolbar's measurement only decides *how many* fit.
* **The DOM order is the order of record.** `tm-toolbar.js` counts buttons in DOM order and reports
  `OnFitChanged(maxVisible, orderedIds)` — the count **and** the `data-tm-toolbar-item` ids in DOM
  order. `TmToolbar` re-sorts its registered buttons to that order before it resolves the partition, so
  the More menu order and the tie-break ("the later one leaves first") follow what the user sees, even
  for a conditional button inserted *between* existing ones after the first render. A reorder alone
  (same count) is reported too.
* **Measurement** is `tm-toolbar.js`: a `ResizeObserver` on the toolbar plus a `MutationObserver`
  (children, class/disabled/rank/pin attributes, and **character data** — a changed label re-measures),
  coalesced into one animation frame, with signature dedup — .NET is called only when the count or the
  order changes, so there is no render loop. A button that left the bar stays in the DOM *collapsed*
  (`.tm-toolbar-item--collapsed`: out of flow, invisible, `inert`, `aria-hidden`) so its width stays
  measurable; that is why widening the toolbar brings it back without a second pass. A bar that cannot
  be measured (no item in the DOM, not laid out) reports nothing rather than "0 fit". The module
  detaches itself when its root leaves the document.
* **Items at any depth, and groups.** Every `TmToolbarButton` below the toolbar registers (cascade) and
  is measured wherever it sits. `.tm-toolbar-start` / `.tm-toolbar-actions` are flattened, and so is
  any wrapper that holds items: mark a layout wrapper `data-tm-toolbar-group` (and `role="group"` with
  an accessible name when it is a semantic group) and its children take room individually; the wrapper's
  own padding/border/gap counts as fixed width. Without the flattening a wrapped group would look like
  one fixed block with no items and collapse everything into More.

  ```razor
  <TmToolbar Overflow="ToolbarOverflow.Menu" AriaLabel="Diagram tools">
      <div role="group" aria-label="Edit" data-tm-toolbar-group class="my-group">
          <TmToolbarButton Icon="@IconNames.Undo" Text="Undo" />
          <TmToolbarButton Icon="@IconNames.Redo" Text="Redo" Priority="ToolbarButtonPriority.Secondary" />
      </div>
      <TmToolbarDivider />
      ...
  </TmToolbar>
  ```
* **Dividers that separate nothing** — first or last among the visible content, or directly followed by
  another divider ("Pan | | Find" at 390px) — are marked `data-tm-divider-redundant` by the
  measurement pass and hidden with `visibility` (they keep their box, so the measured width and the fit
  cannot oscillate). The attribute is not observed, so marking never re-triggers a pass.
* **The More menu** is the shared overflow menu (`ActionOverflowMenu`, also the action bar's),
  so it honours the whole host contract of [overlays.md](overlays.md): close-before-invoke,
  first enabled item focused, `aria-expanded` accurate on every close path (a Tab out of an open
  popover menu closes it), focus back to the trigger, a disabled trigger when only disabled items
  remain. A `Danger` button's entry is tinted. On a phone-sized viewport the menu is the shared bottom
  sheet with 44px entries; an editor/formatting toolbar sets `OverflowPresentation="Popover"` so the
  user's selection survives (see the F4 decision in overlays.md). The menu survives a reorder while
  open (resize, rotation): its items are keyed and every item carries a stable capture.
* **Focus survives a re-layout.** When the focused bar button collapses into More, focus moves to the
  More trigger (else to the roving stop) instead of dropping to `<body>`; a focus that is still valid or
  that the user moved elsewhere is left alone.
* The toolbar measures **its own width** — an editor panel in a 390px column collapses on a 1440px
  desktop.

## Layout contract (breaking since F4)

The toolbar is a **size container** (`container-type: inline-size`), which means it has **no intrinsic
inline size** of its own: in a context that sizes its children to their content — a flex item without a
basis/grow in a `space-between` row, an `inline-block`, a float, an absolutely positioned box, an `auto`
grid column, `fit-content` — it collapses to zero width. Give the host room:

```css
/* before (F3): the toolbar sized itself to its buttons */
.page-header { display: flex; justify-content: space-between; }

/* after: the toolbar fills the slack (or give it an explicit width) */
.page-header > .tm-toolbar { flex: 1 1 auto; min-width: 0; }   /* or: width: 100%; */
```

Inside a `TmToolbar`, `.tm-toolbar-btn` is `flex-shrink: 0` (a button keeps its natural width and leaves
the bar into More instead of squeezing into its touch-target `min-width`) and a coarse pointer gives
buttons and the More trigger a 44px target. Both rules are scoped to `.tm-toolbar`: a standalone
`TmToolbarButton` (`TmDiagramEditor`, `TmModelingDiagramPreview`) keeps its pre-F4 flex behaviour.

## Labels

* `Inline` (default) puts the text next to the icon; `Below` stacks it under the icon (the ribbon
  style of the tablet design); `Hidden` omits the text span entirely — the text stays the
  accessible name and the tooltip.
* `Labels="Auto"` (default) shows labels and drops them on a toolbar **narrower than `TmBreakpoints.Sm`
  (640px), measured on the toolbar** with a container query (`@container tm-toolbar (width < 640px)`).
  There is no JavaScript and no flash; the text remains the `aria-label`. This is independent of a
  shell's custom thresholds by design: a host whose shell uses other thresholds passes `Labels`
  explicitly from the cascaded layout context —

  ```razor
  <TmLayoutObserver Breakpoints="@_shellBreakpoints">
      <LayoutContent Context="layout">
          <TmToolbar Labels="@(layout.IsMobile ? ToolbarLabels.Icons : ToolbarLabels.IconsWithText)"
                     Overflow="ToolbarOverflow.Menu" AriaLabel="Tools">...</TmToolbar>
      </LayoutContent>
  </TmLayoutObserver>
  ```
* `Labels="Icons"` never shows icon-button text; `IconsWithText` always does. A button's own
  `LabelPosition="Hidden"` wins over the toolbar. A text-only button always keeps its text.

## Keyboard and accessibility

The toolbar carries the `toolbar` role and a **roving tabindex**: one tab stop, ArrowLeft/ArrowRight
(swapped in a right-to-left toolbar), Home and End move between the enabled controls that are on the
bar — collapsed copies, disabled buttons and the open More menu are skipped. Tab leaves the toolbar.
Inside the menu ArrowUp/ArrowDown/Home/End and typeahead come from `tm-menu-nav.js` (see
overlays.md). Because the toolbar ships the mechanism, `ToolbarRoleGuardTests` allow-lists exactly
this component (and asserts that `TmToolbar.razor` uses `ToolbarInterop`, whose source carries the module
path and the `attach` call); any other claim of the role without a roving mechanism still fails the guard.

**Hooks for custom content.** The roving set is `button.tm-toolbar-btn`, `button.tm-toolbar-more` and any
element marked `data-tm-toolbar-control`: put the attribute on a custom focusable (a `TmDropdown` trigger,
a select, a toggle) to make it one stop of the roving set instead of a stray Tab stop. A host that renders
the toolbar row inside its own wrapper marks the measured row `data-tm-toolbar-row` (default: the
`.tm-toolbar` root itself).

## Migrating from the DocumentEditor toolbar (DE-1.2)

The DocumentEditor keeps its own `ToolbarItemPriority` and `toolbar-overflow.mjs` (it measures which
ribbon commands are scrolled out of view). The core generalises that mechanism — `ToolbarButtonPriority`
and `tm-toolbar.js` (fit count + DOM order, signature dedup, rAF coalescing,
`ResizeObserver`/`MutationObserver`). The DocumentEditor migrates onto it in its own plan; nothing in the
DocumentEditor changed here.

| DocumentEditor | Core |
|----------------|------|
| `ToolbarItemPriority` | `ToolbarButtonPriority` (`Primary` / `Secondary` / `OverflowOnly`, plus `Pinned` for the trailing CTA) |
| `toolbar-overflow.mjs` scroll measurement (which commands are scrolled out of view) | `tm-toolbar.js` fit count: how many buttons FIT, reported with the DOM order |
| `TmDocumentToolbarOverflowMenu` | `ActionOverflowMenu` (the shared host, over `TmOverlayPanel Role="menu"`) |
| a menu that must not collapse the editor selection | `OverflowPresentation="PanelPresentation.Popover"` (editor/formatting toolbars; F3-PANEL-PRESENTATION) |

### Core prerequisites DE-1.2 / DiagramEditor adoption still needs

The following are **not** in the core yet and gate the adoption (recorded as core follow-ups):

1. **A collapsible non-button item contract** — `Select`, `ColorPicker`, `GridPicker`, `Toggle` and similar
   custom controls need a `Priority` and a menu-entry template, so they can leave the bar like a button
   (today only `TmToolbarButton` registers).
2. **Pressed / checked state** — `TmToolbarButton.Pressed` → `aria-pressed` → `TmActionItem.Checked` →
   `role="menuitemcheckbox"` in the menu. The DiagramEditor's tool modes lose their active state in More
   until this exists.
3. **Group headers and separators in `ActionOverflowMenu`** — a flat list loses the "Edit / View / Insert"
   structure the ribbon groups carry.
4. **Toolbar groups** — done here (`data-tm-toolbar-group`, see above): grouped rows measure and collapse
   correctly.