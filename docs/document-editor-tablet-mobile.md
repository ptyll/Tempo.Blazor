# Document editor - tablet and phone (redesign plan 146c7e1b, phase 8)

`TmDocumentEditor` lays itself out from its **own container** (`LayoutMode=Auto`, `data-layout` on the root, breakpoints 640 / 1024 - they
intentionally differ from `TmEditorShell`, which uses 768 / 1200). A forced `LayoutMode` pins a mode for hosts and tests. `data-layout` is the one
source of truth: the markup, the CSS and the tests all key off it, never off a viewport media query.

| | Desktop (>= 1024) | Tablet (640 - 1023) | Phone (< 640) |
| --- | --- | --- | --- |
| Regions | `nav` \| `doc` \| `inspector` | `doc` + the inspector as a side sheet in the SAME cell | `doc` only |
| Editing | full | full (decision G2) | viewing + light edits (decision G2) |
| Inspector | docked, open by default | ONE side sheet over the document, closed by default, opened by the edge toggle | sheets of the floating root (core bottom sheets), closed by default |
| Find | the inspector's context | the same side sheet's context (replaces the tabs) | its own full-height sheet |
| Image / table | the inspector's context | the same sheet's context | a short bottom sheet on request ("Properties") |
| Outline / Pages | the left column | tabs of the side sheet (text tabs, like the dock) | icon tabs of the inspector sheet / "More" |
| Header | full | full, labels dropped | one row: title, state icon, Search, Undo, Redo, kebab |
| Ribbon | all tabs, priority overflow | all tabs | Home, Insert, Review, View; one tools row; "More" is a sheet |
| Chrome at the bottom | status bar | status bar | status bar + the action bar |

## Decision G2 - what a phone offers

On a phone: reading, navigation (Outline / Pages in a sheet), Find (and replace), comments, accepting / rejecting revisions, versions (viewing and
creating), B / I / U, lists, alignment, link, inserting an image and a symbol, undo / redo, save and the print preview. Filling a content control stays a
light edit.

From a tablet up only: page layout, managing styles (create, rename, delete), equations, the table and cell properties, the advanced image properties
(position, anchor, order), authoring of signing fields and comparing against sources. A phone gets the **short set** of an image (wrapping, alignment,
alternative text) and of a table (alignment) in a bottom sheet.

## One side sheet (tablet)

The inspector region shares the document's grid cell and overlays it (`pointer-events: none` on the region, the sheet itself takes the hits), so opening
or closing a panel never changes the width the page is laid out in. At most one thing is open in it: the tabs, an object (image / table) or find - the
editor swaps the content (`data-context` on the panel), it never stacks a second surface.

## Sheets (phone)

A phone has no inspector region. The inspector, find and the object sheet are `TmDocumentFloatingPanel`s of the floating root rendered with
`LayoutMode=Mobile` - the core `TmOverlayPanel` bottom sheet (top layer, focus, swipe, safe area). Only one is open at a time. Find and the inspector are
full height (`data-floating-kind="find"` / `"inspector"`), the object sheet keeps the content height. A finger on an object never opens a sheet by itself.

## Action bar (phone)

The core `TmMobileActionBar` (`Placement=StickyContainer`, so it sticks inside the editor, never against the viewport): Comments (with the review count as
a badge - the pending revisions plus the open comments; the summary banner is not rendered on a phone), Print preview, the primary Save and "More" (Find,
Revisions, Versions, Outline, Pages, Properties; Print to PDF is behind the header kebab only). The host's `HeaderActions` live behind the header kebab.

## The phone Home row

The row has room for B I U and a few 44 px controls. The core toolbar drops by count (lowest rank first, among equals the LATER control first) and never
backfills a gap, so the phone ranks are: B I U, the alignment menu and the bulleted list `Primary`; EVERY other command that goes through `CorePriority` /
`PhonePrimary` (link, comment, table, image, strikethrough, colours, the wide style / font selects) is `Secondary` on a phone and leaves first. The row's gutter
is 2 px (8 px left a 320 px row a few pixels short of the alignment menu and an empty strip). Result: a 320 px row is B I U + alignment, a 390 px row B I U +
alignment + the list. The published `data-priority` stays the registry's rank. The action bar's tiles take the gutters back below a 22.5 rem container so
"Comments" is never broken mid-word.

## Registry scope

`DocumentEditorCommandRegistry.PhoneRestrictedCommands` is `insertEquation`, `cellProperties` and `compareDocuments` - the REGISTERED commands of the
tablet-and-up set; they are disabled on a phone layout whatever the context says. Page layout and the style management are not registry commands: the
phone ribbon does not render them.

## Not on a phone, on purpose

The compare dialog (comparing against sources: current / version / id / DOCX) is **not** offered on a phone (decision G2); a phone reads the result of a
version comparison (the diff, Unified, with the Older / Newer / Differences switch). Its screenshots therefore exist for the tablet and the desktop only. The
status bar of a phone keeps the save state only; the page / word counts, the zoom and the fullscreen switch are tablet-and-up. A full-width 1024 px
editor resolves Desktop on its own (>= 1024 of the editor's OWN width); pin `LayoutMode=Tablet` to see the tablet chrome at that width.

## Page width

The page is fitted to the width of the column by the canvas ENGINE (`PageFitMode`, default `Auto`), in every layout and not only on a tablet or a
phone: a container narrower than its page (plus gutters) shows the page as wide as the column - one text column, no sideways scrolling - and a column
that holds the page shows it at 100 percent (the fit never enlarges). The engine's resize observer re-applies the fit on every width change (a
rotation, the keyboard dismissing, a host resize), so Blazor keeps no pending flag; a layout switch asks the engine to resolve it once more because the
padding of the column belongs to the layout. The reader's own zoom wins over the fit until "Page width" / Reset zoom; a sheet never refits (it overlays
the column). `PageFitMode="Off"` restores the old fixed zoom, `FitWidth` makes the page always follow the column. The reader can always get back: the status-bar zoom menu entry "Automatic", the View tab's "Automatic zoom" and Ctrl+0 run the engine's `resetZoom`. A desktop container whose docked inspector would squeeze the page below 75 percent (about 1024 - 1136 px) keeps the desktop ribbon and navigation and shows only the inspector as the tablet sheet (decision `DE-AUTO-FIT-READABLE-FLOOR`, see the developer guide). Not done in this phase: a phone's reflow to a text column ("fit text column") instead of a scaled page - tracked as remaining work.

## Touch

On a coarse pointer every control of the chrome is at least `--tm-touch-target` (44 px): header buttons, ribbon tabs and tools, side panel tabs and
close, the action bar tiles. The ribbon "More" and the print preview are the core mechanisms (no editor-owned overflow model).

## A bounded frame (`Height`)

With `TmDocumentEditor.Height` set (or in fullscreen, which is the same frame at `100dvh`) the editor is a frame of fixed height instead of flowing
in the page: `data-frame="bounded"` on the root. The tablet and the phone follow the same structure as in the flow, with these differences:

- **The document region is the scroller** (`[data-region="doc"][data-scroll-viewport="true"]`), in both axes. The page itself does not scroll, so the
  status bar is always on screen and the horizontal scrollbar of a page wider than its column (`PageFitMode.Off`, a pinned zoom) is at the bottom of the
  visible workspace, not at the end of the document.
- **The tablet's side sheet** keeps its sticky parking, now inside the frame: it sits at the top edge of the scroll area (the ribbon is outside the
  scroller, so the published ribbon size is 0 there) and caps itself by the frame (`--tm-document-frame-block-size`), never by the viewport.
- **The phone's action bar** is the last in-flow row of the frame, under the workspace (and the status bar, when it has something to say): it does
  not float over the document. The safe-area inset is the core bar's own bottom padding - the editor adds none on top (that would double it). The phone sheets stay in the floating root, outside the size
  container.
- **The on-screen keyboard** covers the bottom of the layout viewport without shrinking it. A bounded phone frame attaches the core
  `tm-visual-viewport.js` to its root (only while it is bounded AND a phone, detached otherwise) and gives the covered gap up as bottom padding
  (`--tm-visual-viewport-inset-block-end`), so the status bar and the action bar stay above the keyboard; the version and compare dialogs opt in to
  `TmModal.TrackVirtualKeyboard`. The floating mini / object toolbar is clipped against the doc-region scroller (hidden as
  `canvas-selection-scrolled-out` once the selection leaves it, clamped inside it while partly visible).
- Everything keys on `data-frame` and `data-layout`, so a frame bounded inside a narrow host is the same on a wide screen; a pinned `LayoutMode` is
  unaffected. See "The bounded frame" in [document-editor-developer-guide.md](document-editor-developer-guide.md).
