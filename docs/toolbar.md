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
| `TmToolbarButton` | `Priority` | `Primary` · `Secondary` · `OverflowOnly` | `Primary` |
| `TmToolbarButton` | `LabelPosition` | `Inline` · `Below` · `Hidden` | `Inline` |

## Overflow = Menu

* **Priority is a rank, not an order.** Buttons render in the order you write them, always — the
  menu too. `Secondary` leaves the bar before any `Primary`; of two buttons with the same priority
  the *later* one leaves first. `OverflowOnly` buttons never appear on the bar and never count
  against the room.
* **One ordering rule.** Which buttons leave is `ActionOverflowLayout.Partition` — the same
  function the F5 `TmMobileActionBar` uses (through `ToolbarOverflowLayout`, which maps
  `Priority` to the rank and `OverflowOnly` to the `Always` pin). The toolbar's measurement only
  decides *how many* fit.
* **Measurement** is `tm-toolbar.js`: a `ResizeObserver` on the toolbar plus a `MutationObserver`,
  coalesced into one animation frame, with signature dedup — .NET is called only when the number
  of buttons that fit changes, so there is no render loop. A button that left the bar stays in the
  DOM *collapsed* (`.tm-toolbar-item--collapsed`: out of flow, invisible, `inert`, `aria-hidden`)
  so its width stays measurable; that is why widening the toolbar brings it back without a
  second pass. A divider whose group collapsed entirely is hidden with `visibility` (it keeps its
  box, so the fixed width does not change and the fit cannot oscillate).
* **The More menu** is the shared overflow menu (`ActionOverflowMenu`, also the action bar's),
  so it honours the whole host contract of [overlays.md](overlays.md): close-before-invoke,
  first enabled item focused, `aria-expanded` accurate on every close path, focus back to the
  trigger, a disabled trigger when only disabled items remain. A `Danger` button's entry is
  tinted. On a phone-sized viewport the menu is the shared bottom sheet with 44px entries; an
  editor/formatting toolbar sets `OverflowPresentation="Popover"` so the user's selection survives
  (see the F4 decision in overlays.md).
* The toolbar measures **its own width** — an editor panel in a 390px column collapses on a 1440px
  desktop.

## Labels

* `Inline` (default) puts the text next to the icon; `Below` stacks it under the icon (the ribbon
  style of the tablet design); `Hidden` omits the text span entirely — the text stays the
  accessible name and the tooltip.
* `Labels="Auto"` (default) shows labels and drops them below 640px of *toolbar* width with a
  container query (`@container tm-toolbar (width < 640px)`, `TmBreakpoints.Sm`). There is no
  JavaScript and no flash; the text remains the `aria-label`.
* `Labels="Icons"` never shows icon-button text; `IconsWithText` always does. A button's own
  `LabelPosition="Hidden"` wins over the toolbar. A text-only button always keeps its text.

## Keyboard and accessibility

The toolbar carries the `toolbar` role and a **roving tabindex**: one tab stop, ArrowLeft/ArrowRight
(swapped in a right-to-left toolbar), Home and End move between the enabled buttons that are on the
bar — collapsed copies, disabled buttons and the open More menu are skipped. Tab leaves the toolbar.
Inside the menu ArrowUp/ArrowDown/Home/End and typeahead come from `tm-menu-nav.js` (see
overlays.md). Because the toolbar ships the mechanism, `ToolbarRoleGuardTests` allow-lists exactly
this component; any other claim of the role without a roving mechanism still fails the guard.

## Migrating from the DocumentEditor toolbar (DE-1.2)

The DocumentEditor keeps its own `ToolbarItemPriority` and `toolbar-overflow.mjs` (it measures which
ribbon commands are scrolled out of view). The core generalises that mechanism —
`ToolbarButtonPriority` (`Primary|Secondary|OverflowOnly`) and `tm-toolbar.js` (fit count, signature
dedup, rAF coalescing, `ResizeObserver`/`MutationObserver`). The DocumentEditor migrates onto it in
its own plan; nothing in the DocumentEditor changed here.
