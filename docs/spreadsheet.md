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

## Sheet tabs

`ShowTabCloseButtons` (default `true`) gates the per-tab ×; deleting always stays available from the
tab's context menu. The active tab is marked by its accent underline. When the strip overflows, a pair
of scroll arrows appears (`tm-spreadsheet-sheet-tabs__scroll-button[data-direction=left|right]`).
