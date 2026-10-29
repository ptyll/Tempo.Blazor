# Spreadsheet

`TmSpreadsheet` is the Excel-like grid of `Tempo.Blazor.Spreadsheet`: an opt-in document header, a
tabbed ribbon with named groups, the formula bar, the canvas grid, the sheet tabs and a status bar.
Dialogs are documented separately in [spreadsheet-dialogs.md](spreadsheet-dialogs.md).

## Ribbon contract

The ribbon is a stable, host-facing contract. Everything below is **additive-only**: new groups,
tabs or attributes may appear, but the values and selectors listed here never change meaning.

| Attribute | Where | Values |
|-----------|-------|--------|
| `data-tm-tab` | every ribbon tab and its `role="tabpanel"` | `Home` · `Insert` · `Data` · `View` · `File` |
| `data-tm-group` | every ribbon group | `clipboard` · `font` · `alignment` · `number` · `cells` · `insert` · `links` · `data` · `view` · `file` · `custom` |
| `data-tool-id` | a custom tool's group and button | the `SpreadsheetCustomTool.Id` |

Group placement per tab:

| Tab | Groups |
|-----|--------|
| `Home` | `clipboard`, `font` (font, size, B/I/U/S, text colour, fill colour, cell borders), `alignment` (horizontal, indent, vertical, merge cells), `number`, `cells` (insert row/column, Delete split), `insert` (quick links/images) |
| `Insert` | `links` |
| `Data` | `data` |
| `View` | `view` |
| `File` | `file` |

`custom` groups render one per `CustomTools` entry, in `Order`, with a case-insensitive `Tab` match.
A `Tab="File"` tool has no File tab while `ShowHeader="true"`; the document header renders it as a
header action (`data-tool-id`) instead.

### Tabs

The tab strip follows the WAI-ARIA tabs pattern: `role="tablist"`, `role="tab"` buttons with
per-instance ids, `aria-selected`, `aria-controls` pointing at the panel, the panel carrying
`role="tabpanel"` and `aria-labelledby` back at the tab, roving `tabindex` (0 on the active tab, -1 on
the rest) and ArrowLeft / ArrowRight / Home / End moving the selection and the focus. `ActiveTab` is
a parameter the component never writes; a click or key press raises `ActiveTabChanged`. A tab that is
not offered (`File` with `ShowFileTab="false"`, or an unknown id) falls back to `Home`.

### Menus

The border picker, both colour pickers and the `Delete` split render their popup through the core
`TmOverlayPanel` (browser top layer). This is deliberate: the ribbon row scrolls horizontally when the
container is narrower than the group set, and a menu inside that scroll box would be clipped. Hosts
therefore get top-layer placement, outside-pointer dismissal and Escape for free, and no ancestor
`overflow` can hide a menu.

## Document header

`ShowHeader` (default `false`) renders `header.tm-spreadsheet__header`: `Title` and the `HeaderStart`
slot on the left; `HeaderActions`, the built-in Open / Download actions and the core `TmToggle`
"Show grid lines" on the right. The built-in Open / Download render only when the host already listens
to `OnOpen` / `OnDownload`; they drive the component's own XLSX import/export. With the header on, the
legacy `File` ribbon tab is not rendered (`ShowFileTab` is forced off). App-shell chrome from the
reference ("Saved", the document switcher, the overflow menu) is host content in the two slots.

## Formula bar and name box

The `fx` mark is permanent. While editing, the bar shows ✓ / ✗ (localized accessible names) with the
same commit/revert paths as Enter / Escape. The name box lists the workbook-scoped named ranges plus
the **active sheet's** sheet-scoped ones (de-duplicated by name, sheet scope wins). Selecting an entry
raises `OnNavigateToRef` with the range name; typing a sheet-qualified reference (`Sheet2!B2`,
`'My sheet'!A1:B2`) switches the active sheet first. The list closes on selection, Escape, an outside
click or when the focus leaves the name box.

## Responsive chrome

The layout is decided by the width of the spreadsheet's **own container**, never by the viewport: the
component is embedded in a Notion block or a modal that can be a phone-width column inside a wide page.
The core `TmLayoutObserver` classifies the box. Its root is the component's own wrapper
`div.tm-spreadsheet__layout` — the named query container `tm-spreadsheet`, and NOT the component root
(`container-type: inline-size` implies `contain: layout`, which would make the root the containing block of
every `position: fixed` overlay and trap the dialogs, the sheets and the context menus inside the card). The
resolved mode is rendered as `data-layout` on the component root, as a
`tm-spreadsheet--layout-{desktop|tablet|mobile}` modifier and as `tm-layout--*` on the wrapper.

The compact rows carry the SAME frozen `data-tm-group` contract as the ribbon (one core `TmToolbarGroup` per
run of commands: `clipboard | font | alignment | number | cells | insert | data | view | file | custom`),
plus `data-toolbar-layout`, `data-toolbar-row` and `data-command-id`. There is **no** `data-tm-tab` below the
desktop breakpoint. Documented deviations from the reference: the file surface is ONE visible command (open)
with the download in "More" (a measuring toolbar item cannot cheaply host a second dropdown), and there is no
"Insert ▾"/"Format ▾" dropdown on the tablet row — the actions stay individual commands so they can overflow
one by one. `LayoutMode` (`TmLayoutMode`, default `Auto`) pins a mode for a host that already
knows its surface and for tests.

| Container width | Chrome |
|-----------------|--------|
| `>= 1024px` (`TmLayoutBreakpoints.Default`) | the tabbed ribbon of the section above, the opt-in document header, the status bar |
| `640–1023px` | **two compact rows** (`data-toolbar-row="primary"` / `"formatting"`) over the core `TmToolbar Overflow=Menu`; no tab strip; the status bar stays |
| `< 640px` | a compact header (`Title` + undo/redo + the host slots), **one** basic-formatting row, the formula bar, the grid in `Comfortable` density, the sheet tabs above the core `TmMobileActionBar`, `height: 100%` (SP3, floored by `--tm-spreadsheet-mobile-min-height` and capped by `100dvh` so the grid scrolls inside the component even in an auto-height host) and no status bar |

The compact rows are a model (`SpreadsheetToolbarLayout.Rows`) mapped onto the core `TmToolbarButton`
and `TmToolbarItem` primitives, so a command that does not fit leaves its row for the toolbar's "More"
menu instead of being clipped. Every command carries `data-command-id` (`undo`, `bold`,
`number-format`, `borders`, …) — that is the host/E2E contract, next to the `data-testid`s.

Mobile is the G2 contract — **viewing plus basic editing**. The basic set is bold/italic/underline,
the number format, the percentage format, the text/fill colours and the alignment; anything else lives
in the row's or the action bar's "More" menu. NOT offered below the tablet breakpoint (full editing
starts at the tablet): Text to Columns, Remove Duplicates, Data Validation (and the invalid-data
circles), the Name Manager, Paste Special, the Format Painter, the cell-border picker, Merge cells, the
thousands separator and the workbook Open / Download. They are absent from the mobile model, so the
toolbar cannot re-offer them.

The phone chrome's `TmMobileActionBar` (Insert / Format / Image + More) carries the sheet, data,
zoom and **summary** groups: the status bar is hidden on a phone, so its zoom controls and its
aggregations are re-offered there (`TmActionItem.Group == "zoom"` / `"summary"`; the summary rows are
inert information).

`LayoutMode` is a pass-through on the overlays too (`TmSpreadsheetFindReplaceDialog`,
`TmSpreadsheetFilterDropdown`, `TmSpreadsheetCanvasGrid`): the filter dropdown becomes a bottom sheet
with 44px rows and a sticky Cancel / OK bar, the grid context menu becomes a `TmDrawer` sheet, and
Find & Replace is the non-modal sheet — all of them keyed off the CONTAINER layout, not the viewport.

The two context menus (`TmSpreadsheetContextMenu`) and every sheet are **siblings** of the layout container —
the grid and the sheet tabs raise `SpreadsheetContextMenuRequest` through `OnContextMenuRequested` and the host
renders the menu (anchored on desktop/tablet, the core `TmDrawer` sheet on a phone, with real
`role="menuitem"` buttons, Escape/backdrop close and the focus back to the grid). While a sheet owns the phone
screen the sticky action bar is removed (`tm-spreadsheet__mobile-bar--suppressed`), because its stacking order
would cover the sheet's footer.

The dialogs and sheets are **siblings** of the layout container. `container-type: inline-size` implies
`contain: layout`, which makes the box the containing block of `position: fixed` descendants: an overlay
rendered inside it would be trapped in the card. Keep every new overlay outside
`.tm-spreadsheet__layout` and read the resolved mode from the component (`DialogLayout`).

## Sheet tabs

`ShowTabCloseButtons` (default `true`) gates the per-tab ×; deleting always stays available from the
tab's context menu. The active tab is marked by its accent underline. When the strip overflows, a pair
of scroll arrows appears (`tm-spreadsheet-sheet-tabs__scroll-button[data-direction=left|right]`).
