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
| Outline / Pages | the left column | tabs of the side sheet | tabs of the inspector sheet / "More" |
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

## Not on a phone, on purpose

The compare dialog (comparing against sources: current / version / id / DOCX) is **not** offered on a phone (decision G2); a phone reads the result of a
version comparison (the diff, Unified, with the Older / Newer / Differences switch). Its screenshots therefore exist for the tablet and the desktop only. The
status bar of a phone keeps the save state only; the page / word counts, the zoom and the fullscreen switch are tablet-and-up. A full-width 1024 px
editor resolves Desktop on its own (>= 1024 of the editor's OWN width); pin `LayoutMode=Tablet` to see the tablet chrome at that width.

## Page width

On a tablet or a phone container narrower than its page the page is fitted to the width of the column (the engine's `fitWidth`) when the canvas is
ready and whenever the layout or the container width changes - requested as a pending flag and run after the render of that layout; the reader's own zoom wins
over the fit and a sheet never refits (it overlays the column).

## Touch

On a coarse pointer every control of the chrome is at least `--tm-touch-target` (44 px): header buttons, ribbon tabs and tools, side panel tabs and
close, the action bar tiles. The ribbon "More" and the print preview are the core mechanisms (no editor-owned overflow model).
