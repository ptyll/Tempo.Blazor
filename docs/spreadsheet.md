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

**File actions (decision `SPREADSHEET-HEADER-FILE-ACTIONS`).** There is deliberately NO dedicated
header command callback (`Func<Task> OnOpenDocument` / `OnDownloadDocument`): `OnOpen` / `OnDownload`
already are the file contract (they render the built-in Open / Download and run the embedded XLSX
import/export), and a second pair with those names would collide. A host that wants its OWN document
picker puts it in `HeaderActions` and keeps the component's file commands off (or keeps both):

```razor
<TmSpreadsheet ShowHeader="true" Title="Budget 2026"
               @bind-Data="_workbook" OnOpen="ImportXlsxAsync" OnDownload="ExportXlsxAsync">
    <HeaderActions>
        @* The host's own picker — the built-in XLSX actions stay reachable next to it. *@
        <TmButton Variant="ButtonVariant.Ghost" Icon="folder-open" OnClick="PickDocumentAsync">
            Open from library
        </TmButton>
    </HeaderActions>
</TmSpreadsheet>
```

Any future dedicated callback is a new requirement (backlog), not a silent addition to this surface.

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

### A host shorter than the chrome (phase 8)

The phone height chain has two knobs and a host may move both:

| Token | Default | Meaning |
|-------|---------|---------|
| `--tm-spreadsheet-mobile-min-height` | `32rem` | the floor of the phone layout, so an auto-height host cannot collapse the grid |
| `--tm-spreadsheet-grid-min-height` | `9rem` | the floor of the **grid** when the host is shorter than the fixed chrome (toolbar + formula bar + column headers + sheet tabs + action bar) |

A host that owns a tight frame (the Notion fullscreen edit modal at 568×320 sets the first knob to `0`
so its Save / Discard footer can never be pushed out of the viewport) leaves the fixed chrome more than
the whole box. Before the second knob existed the grid resolved to ~0px and the editor showed **no
rows at all** — editable in theory, invisible in practice. Now the grid keeps its floor and the phone
root becomes the scroll container (`overflow-y: auto` on `.tm-spreadsheet--layout-mobile`), so the rows
are reachable by scrolling; a host tall enough for chrome + floor never sees a scrollbar. Guarded by
`SpreadsheetDesignTokenTests.MobileLayout_KeepsAUsableGridFloor_WhenTheHostIsShorterThanTheChrome` (CSS
contract) and `SpreadsheetPhase6ResponsiveE2ETests.Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView`
(grid ≥ 96px and reachable by scrolling the phone root, at 844 and 320 px of viewport height).

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

### Dialogs at 320 px

Every dialog is on the core `TmModal` (G6) and the phone presentation is decided by the viewport scope
(`MobilePresentation.Auto` / `AutoFullscreen`), so a 320 px phone gets the fullscreen panel. The three
dialogs that carry the most content were measured at 320 and 390:

- **Name Manager** — the phone card puts the name (+ the live value) on the left and the reference
  formula, the scope chip and the comment on the right (the reference's two columns). At 320 the card
  stacks: the name stays ONE ellipsised line, the **"Refers to" line WRAPS** (`overflow-wrap: anywhere`,
  clamped to two lines) with the full formula in the element's `title`/`aria-label`, and the scope chip
  moves up under the name. The threshold is `@container (width < 22rem)` measured on the DIALOG's own
  width — 320 px = 20rem flips it, a 390 phone (24.4rem) keeps the reference's two columns. Decision:
  `SPREADSHEET-NAME-MANAGER-320-WRAP`.
- **Sort** — on a phone the toolbar is the reference's TWO-column grid: "Add level" on the left, the
  "My data has headers" / "Case sensitive" checkboxes stacked on the right. Below `20rem` of the
  dialog body (320) the options stack UNDER the button instead — readability wins over the reference on
  the narrowest phone. The desktop/tablet table row is unchanged.
- **Format Cells** — the category is a `TmSelect` ABOVE the options on a phone (the reference's 140 px
  listbox column cannot be read at 320), the tabs wrap, and the border tab is not offered (G2).

The Name Manager, Sort and Format Cells 320/390 flow is guarded by
`SpreadsheetPhase7cDialogs320E2ETests` (`scrollWidth == clientWidth` inside the modal body and the
page, the footer visible, no clipped control) plus the screenshots
`__screenshots__/spreadsheet/phase7c-{name-manager,sort,format-cells}--mobile-320.png` and
`phase7c-sort--mobile-390.png`.

**Data Validation breakpoint.** The `label | field` grid of `TmSpreadsheetDataValidationDialog` appears
from `@container (width >= 640px)`, measured on the modal OVERLAY, because the dialog stays
`ModalSize.Medium` by design (its body is ~470 px even on a 1440 viewport, so a body-level container
would never reach 640 px). `DataValidationDialog_KeepsMediumSize_ForOverlayBreakpoint` guards the size:
if the dialog ever grows to Large/XLarge, recompute the threshold before shipping the change.

## Sheet tabs

`ShowTabCloseButtons` (default `true`) gates the per-tab ×; deleting always stays available from the
tab's context menu. The active tab is marked by its accent underline. When the strip overflows, a pair
of scroll arrows appears (`tm-spreadsheet-sheet-tabs__scroll-button[data-direction=left|right]`).

## Touch and pen

The canvas runs on the shared core pointer helpers — `pointer-drag.js` for the single pointer and
`pointer-gesture.js` `attachPinch` for two fingers (`npm run test:overlay`). The package never
implements a drag or a pinch of its own; the two modules load through a dynamic `import()` resolved
from `document.currentScript.src` (classic script, see [touch-interaction.md](touch-interaction.md)).
The gesture DECISIONS are pure functions in `spreadsheet-canvas.js` (`_internals.resolveTouchTap`,
`resolveTouchDragAction`, `resolveLongPressAction`, `resolvePinchZoom`, `resolveAutoFillHitSize`,
`resolveTouchHandleRects`, `resolveFilterButtonHitRect`, `resolveCellInputMode`) and are covered by
`npm run test:spreadsheet`; the fingers themselves by the Playwright touch lane
(`SpreadsheetPhase7TouchE2ETests`).

| Gesture | Result | Non-drag / keyboard alternative |
|---|---|---|
| tap | selects the cell | the arrow keys (roving focus) |
| double tap (≤ 320 ms, ≤ 24 px) | opens the editor with `inputmode` from the cell type (`decimal` for a numeric or percent cell, `text` for text/date) | `Enter` / `F2` / typing a character |
| long press (500 ms) | the cell menu at the finger (phone: the `TmDrawer` sheet; tablet/desktop: the anchored menu). A 400 ms release-click guard keeps the lifting finger from closing it | `Shift+F10`, the context-menu key, right click |
| one-finger drag on a cell | the browser's **native pan**; the canvas starts no drag-select from a finger | scrollbars, PageUp/PageDown |
| tap the cell next to the selection | selects that cell (the drops never cover it) | the arrow keys |
| drag a ROUND corner drop (`start` = top-left, `end` = bottom-right) | extends the selection, keeping the anchor — drag-only, a tap on a drop does nothing | **Shift+arrow** |
| drag the fill handle (bottom-right) | fills | **Fill down / Fill right** in the cell menu and **Ctrl+D / Ctrl+R** (the C# `AutoFillCommand`, one undo step) |
| two-finger pinch | one clamped 10 % zoom step (50–200 %) through `OnZoomChanged` | the status-bar zoom buttons, the phone's More → zoom |

`touch-action: pan-x pan-y` on `.tm-spreadsheet-canvas-grid` is what makes all of this unambiguous:
one finger pans, the browser's page pinch-zoom is off over the grid, and the JS pinch owns the zoom.
The exception is the **DOM touch-handle layer**
(`.tm-spreadsheet-canvas-grid__touch-handles`, three `[data-touch-handle]` boxes: `start`, `fill`,
`end`): these are real elements because `touch-action: none` can only be set on an element.
`spreadsheet-canvas.js` positions them over the painted selection (`syncTouchHandles`), CLIPPED to the
grid body — a box that would have to be shifted onto the selection to fit is hidden instead — and pairs
each with a `clipTouchHandleRect` pure helper. The two corner drops are drag-only and sit on the
selection's corners (the `end` one just outside the fill handle); they are `aria-hidden`, not
focusable — the keyboard paths above are the accessible ones — and the whole layer is hidden on
`(pointer: fine)`, so a desktop host sees no new interactive element and the mouse keeps its painted
handles and coordinate hit test. The earlier four EDGE STRIPS were removed: along the selection's edge
they covered the selected row/column on a phone (30 px rows vs a 44 px finger box) and swallowed the
taps and long-presses meant for the cell itself.

**Ctrl+D / Ctrl+R are claimed by the canvas** (`isCanvasShortcutCommand`, node-tested): the key is
`preventDefault`ed and forwarded to .NET, so Ctrl+D does not open the browser's bookmark dialog and
Ctrl+R does not reload the page with the grid on it. A read-only grid returns before the keyboard
command path, so an Embedded block keeps the browser's chords. The fill itself follows Excel: Fill down
copies the selection's top row into EVERY column (Fill right its left column into every row), the
source row/column is never re-written, the source cell's FORMAT is copied into the filled cells
(`SpreadsheetCellStyle.CopyFrom`), and the whole operation is one `BatchCommand` — one undo step. A
single-cell selection copies the cell above/left and an empty source is a no-op.

## Embedding (Notion block, modal, read-only host)

`TmNotionSpreadsheetBlock` renders `Mode=Embedded`, i.e. a read-only grid: it opens **no** cell menu
(the gesture is blocked by `IsReadonly`), so there is no long-press sheet and no fill. The host cell
menu is built from `Mode` too — a read-only spreadsheet offers only Format Cells, Copy, column width
and row height, never Paste/Cut/Fill/insert/delete/Clear. `TmNotionSpreadsheetEditModal` hosts the
editable grid inside the Notion fullscreen modal: the gestures are the normal ones, the pinch zooms the
SHEET (the modal does not zoom), and the phone sheet renders as a sibling of the layout container so
the modal's stacking context cannot clip it. Scroll chaining: the menu contains its own overflow
(`overscroll-behavior: contain`); the grid deliberately keeps the platform's default chaining, because
a nested editor that swallows an edge scroll traps the page.

### `TmSpreadsheetCanvasGrid.SuppressFocusRestore` — a host surface that owns the focus

`TmSpreadsheet` renders the grid with `SuppressFocusRestore="@IsHostSurfaceFocused"`, i.e. `true` while
one of the component's own surfaces (the anchored context menu, the phone sheet, a resize dialog) is
open. A host that renders `TmSpreadsheetCanvasGrid` **directly** and draws its own overlay over the grid
must pass the same flag itself:

```razor
<TmSpreadsheetCanvasGrid @ref="_grid" Sheet="_sheet"
                         OnContextMenuRequested="OpenMyMenu"
                         SuppressFocusRestore="@(_myMenu is not null)" />
```

Why it is a parameter and not a timing fix: a pointer activation that opens a surface arms the grid's
focus restore, and the grid and the surface run `OnAfterRenderAsync` in the SAME batch, so the grid's
"did the focus move away?" probe answers before the overlay focused its first entry and the stale answer
stole the caret back (phase 7d — the context menu's ArrowDown / Home / End were dead in a real browser,
green in bUnit). With the flag the precedence is explicit: the host surface owns the focus. An EXPLICIT
navigation (`NavigateToCellAsync`, the name box, Find next) still wins over both, and a
pointer-originated restore keeps using the atomic `tmSpreadsheetCanvas.focusGridUnlessFocusMovedAway`
(check and focus in ONE task, never a probe followed by `FocusAsync`).

## Scripts and CSP

`spreadsheet-canvas.js` (and `spreadsheet.js`) now dynamic-`import()` the shared core helpers, so a host
must serve **`_content/Tempo.Blazor/js/`** as well as the package's own `_content/Tempo.Blazor.Spreadsheet/js/`
— the helpers sit in the core package folder (`_content/Tempo.Blazor/js/pointer-drag.js` and
`pointer-gesture.js`) and are resolved from the script's own URL. A per-file CSP hash must therefore
list `spreadsheet-canvas.js` **and** `pointer-drag.js` + `pointer-gesture.js`; a host-wide nonce for the
served origin covers all three. When the helpers cannot be loaded the grid keeps its mouse interaction
and logs one warning ("the pointer helpers ... could not be loaded"): touch gestures (tap, double tap,
long press, pinch, the corner drops) are OFF, so a touch-only kiosk must serve them.

The hit areas follow the density: `Density=Comfortable` (the `Auto` default on a coarse pointer) grows
the painted fill handle **and** its hit box, gives a coarse pointer a 24 px fill-handle box, and scales
the header gutters (row header 56 px, column header 28 px), which is also what makes the auto-filter
button a finger target (its HIT rect reaches the touch floor inside its own column). The `Density=Auto`
resolution is re-run whenever the `(pointer: coarse)` media query flips, so attaching a mouse to a
tablet does not keep the touch density.

### Density — the gutter scale and the hit areas

`Density` (`SpreadsheetDensity.Auto|Compact|Comfortable`, `Auto` by default) is a RENDER scale: the
workbook model (`DefaultRowHeight`, a per-row height, every column width and every XLSX value) never
changes. `SpreadsheetDensityScale` is the single source of truth for the multipliers and the canvas
frame carries the RESULT, so the component geometry, the canvas paint and the hit-testing cannot drift:

| Scale | Compact (fine pointer) | Comfortable (`Auto` on a coarse pointer, or forced) |
|---|---|---|
| row height | 20 px | 30 px (x1.5) |
| cell font | 10 pt | x1.25 |
| fill handle | 10 px painted / **10 px hit box** on a fine pointer | x1.4 painted, **24 px hit box** on a coarse pointer |
| row-header gutter (`RowHeaderWidth`) | 40 px | **56 px** (x1.4) |
| column-header height (`ColumnHeaderHeight`) | 20 px | **28 px** (x1.4) |
| header label font (`--tm-spreadsheet-header-font`) | the token exactly (11 px) | the same token x 1.25 (**13.75 px**) — scaled in `drawHeaders`, the token itself is never rewritten |
| auto-filter button HIT rect | the painted glyph rect exactly | at least the 44 px touch floor, clamped into its own column and gutter |

Consequences a host or a test must know:

- **Never compute a cell position from the 40/20 constants.** The engine exposes two JS getters for
  exactly this, so a spec or an integration test reads the live geometry instead:
  `tmSpreadsheetCanvas.getCellCenter(root, row, col)` returns `{x, y}` in the ROOT ELEMENT's coordinate
  space (what a Playwright `Locator.ClickAsync(Position)` wants; `{-1,-1}` for an unknown cell) and
  `tmSpreadsheetCanvas.getGridGutters(root)` returns `{ rowHeaderWidth, columnHeaderHeight, density,
  gutterScale }`. The grid's hit-testing reads the same frame gutters (`resolveContentPoint`), so a
  scaled gutter moves the painted cells and the hit path together.
- **The active header strip is OPAQUE in dark mode.** The light token chain resolves through
  `--tm-color-primary-subtle` (`rgb(from ... / 0.15)`); the package's theme layer
  (`wwwroot/css/theme-spreadsheet-dark.css`, imported by the package bundle) re-mixes it as
  `color-mix(in srgb, var(--tm-color-primary) 22%, var(--tm-spreadsheet-header-bg))`. The canvas mixes
  that `color-mix()` itself (`parseColor` / `resolveColorToken`), so the strip is opaque even where
  `fillStyle` would refuse the function. A host that imports the individual CSS files instead of the
  bundle must import that theme file too.
- **A live theme flip repaints the canvas without a resize.** The palette is a snapshot of the
  resolved tokens, taken on init and on every resize, so flipping `data-theme` / `data-tm-theme` on a
  live page used to recolour the chrome while the canvas kept the old colours. The engine now watches
  both attributes on `documentElement` (one `MutationObserver` per grid instance, disconnected in
  `dispose`) and rebuilds the palette + requests a FULL redraw on every change. Guarded by the node lane
  (`themeAttributeChange_RebuildsThePalette`, `npm run test:spreadsheet`) and by the E2E
  `Phase7c_LiveThemeFlip_RepaintsTheCanvasWithoutResize` (no viewport nudge).
- **Performance evidence**: the wall-clock A/B of the mouse selection drag (v2.9.0 against the current
  canvas, alternating arms, median + IQR) lives in `eng/release-evidence/spreadsheet-perf-<yyyyMMdd>/`
  and is produced by `SpreadsheetPerformanceE2ETests.SelectionDrag_AB_WithinBudget`
  (`TestCategory=Performance`). The budget is `median(current) <= 1.10 x median(v2.9.0)`; an IQR above
  30 % of the median makes the spec report `Inconclusive` with the numbers instead of passing. Each run
  writes `selection-drag-ab-<HHmmss>.json` and `summary-<HHmmss>.md`, so a same-day re-run never
  overwrites an earlier sample. **Current evidence (20261029): Inconclusive** (noise 50–63 %, ratio sign
  flipped between two runs) — awaiting a run on an idle machine or the perf lane; the `v2.9.0` arm has
  no header-highlight layer, so it is not a like-for-like arm for the selection-drag scenario.

`prefers-reduced-motion` is honoured through the shared helpers (`reducedMotion` in every callback);
the grid's touch path has no animation of its own to disable.
