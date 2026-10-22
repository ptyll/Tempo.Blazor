# PdfViewer & Annotator — the `Tempo.Blazor.PdfViewer` contract

The `Tempo.Blazor.PdfViewer` package (static-asset base `_content/Tempo.Blazor.PdfViewer/`)
ships a PDF.js v5 viewer, an annotator and the shared comment-thread cards. This page is the
package's stable contract: what hosts may bind, which DOM/test-id surfaces are frozen and how to
migrate from the pre-redesign (2.9.0) shapes. Agent-facing summary: the **TmPdfViewer Features**
section in [AGENTS.md](../AGENTS.md); toolbar-group vocabulary: [toolbar.md](toolbar.md); touch:
[touch-interaction.md](touch-interaction.md). The demo pages are `/pdf-viewer`, `/pdf-annotator`
and `/redaction`.

## Installation

```bash
dotnet add package Tempo.Blazor.PdfViewer
```

```csharp
builder.Services.AddTempoBlazorPdfViewer(); // registers the package (implies AddTempoBlazor())
```

The package's JS is three **classic scripts** the host page must include (the components degrade
to an `<embed>` fallback without `pdf-viewer.js`):

```html
<script src="_content/Tempo.Blazor.PdfViewer/js/pdf-viewer.js"></script>
<script src="_content/Tempo.Blazor.PdfViewer/js/pdf-annotator.js"></script>   <!-- TmPdfAnnotator -->
<script src="_content/Tempo.Blazor.PdfViewer/js/pdf-redaction.js"></script>   <!-- TmRedactionLayer -->
```

`pdf-viewer.js` is the `window.tmPdfViewer` facade; it dynamically imports PDF.js v5
(`pdf.min.mjs` + worker) and its node-tested helper modules (`pdf-viewer-size.mjs`,
`pdf-viewer-render.mjs`, `pdf-viewer-fit.mjs`, `pdf-viewer-continuous.mjs`,
`pdf-viewer-download.mjs`) relative to its own URL, and pulls the shared core gestures
(`pointer-gesture.js`) across the split-package static-asset boundary — never a local copy.

## Components

| Component | Purpose |
|-----------|---------|
| `TmPdfViewer` | The viewer: canvas rendering, toolbar, thumbnails, search, text layer, rotation, continuous scroll, optional annotations |
| `TmPdfAnnotator` | The annotator: `TmPdfViewer` + annotation tools on one merged toolbar, laid out by `TmEditorShell` |
| `TmPdfAnnotationThreadPanel` | The viewer's comments panel (docked panel / side sheet / bottom sheet) |
| `TmPdfAnnotatorPanel` | The annotator's thread panel (same cards, annotator chrome, `ShowChrome`) |
| `TmPdfThreadList` | Shared thread-card loop (also renders the quoted text + sticky reply composer) |
| `TmPdfThreadCard` | Card header: color badge, kind, preview, author · time, page ref, reply count, Resolved chip, chevron |
| `TmPdfCommentItem` | Comment body: `TmAvatar`, "(Edited)" marker, ⋯ menu (Edit / Delete), relative time |
| `TmPdfComposer` | Textarea + 32px-minimum action row; sticky at the expanded thread's bottom; Ctrl+Enter submits |
| `TmPdfAnnotationLayer` / `TmPdfAnnotatorLayer` | Page-overlay marker layers (per page in both view modes since FZE-13; the annotator layer stays on the pinned SinglePage viewer) |
| `TmRedactionLayer` | Redaction stage on its own viewer; pinned to `SinglePage` |

## TmPdfViewer

### Responsive layout

The viewer measures **its own container** (`.tm-pdf-viewer` is a named query container,
`container-type: inline-size`) through a wrapping `TmLayoutObserver` — breakpoints are the core
`TmBreakpoints` (`Sm` 640, `Lg` 1024):

- **Desktop** (≥1024px): the docked chrome — toolbar row, optional header, docked thumbnails
  sidebar (open) and the comments panel (open when annotations are enabled).
- **Tablet** (640–1023px): the thumbnails start **collapsed** (the "Pages" toggle reopens them),
  toolbar labels render **below the icons**, and the comments panel is a `TmSidePanel` side
  sheet — closed by default, restoring focus to its toolbar toggle on close.
- **Mobile** (<640px): a compact single-row toolbar (prev chevron · page input · next chevron,
  compact `N/M` count, bare-`%` zoom trigger) with a "More" menu for the low-priority groups
  (Rotate, Open, Download, the continuous toggle — `OverflowOnly` priorities), an in-flow
  `TmMobileActionBar` ("Pages | Search | 3 comments" — only enabled features render tiles; the
  comments tile reuses the plural `TmPdfViewer_CommentsCount` wording),
  the thumbnails / comments / zoom presets / search as bottom sheets, and `ZoomMode=Auto`
  computing **fit-width**.

`LayoutMode` (`TmLayoutMode.Auto` by default) pins a mode for tests and hosts; a forced
cascading `TmLayoutContext` ancestor (a `TmLayoutObserver` with a forced mode, the same
`TmLayout.Resolve` pattern as `TmEditorShell`) wins over the viewer's own measurement. Panel
defaults re-apply when the layout flips until the user has touched the panels themselves (an
internal flag resets per document load); reopening the thumbnails resets the JS thumbnail cache.
`Height` accepts any CSS value including `100%`/`100dvh` — they resolve through the layout
host's flex chain (`min-height: 0`).

### Toolbar contract

The toolbar is the core `TmToolbar` (`Overflow=Menu`, priority-based "More"). The root keeps
`.tm-pdf-viewer__toolbar`, `role="toolbar"`, the localized "PDF controls" aria-label and the
`pdf-viewer-toolbar` test id. Groups carry the stable **`data-tm-group`** values:

| Group | Contents |
|-------|----------|
| `navigation` | Prev, editable page `<input type="number" inputmode="numeric">` (Enter/blur navigates; out-of-range reverts), Next, `/N` count |
| `zoom` | Zoom out, `TmDropdown` presets (50/75/100/125/150/200 % + Fit width + Fit page), zoom in |
| `rotate` | Rotate counter-clockwise + rotate clockwise (`AllowRotation`) |
| `view` | The single/continuous segmented control (`ShowViewModeToggle`) |
| `panels` | The "Pages" thumbnails toggle |
| `search` | The search toggle (`ShowSearch`) |
| `comments` | The comments toggle with the live open-thread count as its label ("1 comment" / "3 comments" — the `TmPdfViewer_CommentsCount` plural family; the toggle keeps its text even in the icon-only toolbar) |
| `export` | The trailing file links (Download primary, Open secondary) + host `ToolbarEnd` |

The annotator adds its `tools` group and its panel toggle into the same row. Where a group
renders (toolbar / "More" menu / mobile row / header) is a layout detail — hosts key off
`data-tm-group` and the test ids (`pdf-page-count`, `pdf-search-count`, `pdf-viewer-toolbar`,
…), never positional selectors. `ToolbarLabels` (`Icons` default, `Auto`, `IconsWithText`)
controls the visible labels; on a tablet layout they render below the icons.

### Header and slots

`ShowHeader="true"` renders a document header above the toolbar: `Title`, a subtitle
(`Subtitle`, defaulting to a neutral "Pages: N" once loaded — extended with the localized file
size, "Pages: N · 1.4 MB", while `ShowFileSize` is on and a size is known: the host `FileSize`
wins over the PDF.js `getDownloadInfo` report; an explicit `Subtitle` is never augmented), the
`HeaderStart` slot, and the
`HeaderActions` slot after the **Download (primary)** and **Open (secondary)** links — with the
header on, those two move out of the toolbar. `ToolbarStart` / `ToolbarEnd` compose host groups
into the toolbar (this is how `TmPdfAnnotator` merges its tools), and
`SelectionCtaContent` fills the sticky in-flow selection CTA row that sits **outside** the scroll
area (a zoom or scroll never carries it away).

### Search presentation

`SearchPresentation` (`PdfSearchPresentation`, default **`Floating`** since 2.9.1) decides how
the search UI presents on desktop and tablet: **Floating** renders a `role="search"` card
anchored top-right over `.tm-pdf-viewer__body` — outside the scroll wraps, never covering the
toolbar and painted above the docked panels (`pdf-search-card` test id, elevated surface +
card shadow/radius tokens, a `prefers-reduced-motion`-aware entrance). The card keeps every
stable `pdf-search-*` test id; Enter walks forward, Shift+Enter backward, and Escape (from the
input, the prev/next/clear buttons or ✕) closes it and returns focus to the toolbar's search
toggle — or, when the toggle has overflowed into the "More" menu, to the More trigger, else to
the viewer root. Opening the card (`ToggleSearchAsync` / `OpenSearchAsync`) moves the caret
into `pdf-search-input` after the render. A zero-match search renders the localized
`TmPdfViewer_SearchNoMatches` verdict inside `pdf-search-count`, flags the input
`aria-invalid`, and both live in an always-present `role="status"` live region. While the card
is open the scroll wraps keep natively aligned content (block:'start' page navigation and the
active search match) clear of the card strip (`scroll-padding-top` + the measured card rect in
`_scrollToActive`). **Inline** renders the pre-2.9.1
full-width bar under the toolbar unchanged, and a mobile layout always keeps the sheet-style
bar in both presentations.

### Zoom modes

`PdfZoomMode` decides the scale:

| Mode | Behavior |
|------|----------|
| `Auto` (**default**) | Fit-width on a mobile layout, the bound `Scale` everywhere else; a resize only recomputes while the layout stays mobile |
| `Custom` | User/binding-driven — the pre-Auto behavior at every width |
| `FitWidth` | JavaScript computes the scale so the current page fills the container width; a ResizeObserver keeps recomputing it on resize |
| `FitPage` | Same, but the whole page fits the container |

A fit scale MAY fall outside the 50–200 % presets (the exact container ratio wins) and is
clamped to the absolute 0.1–5 bounds. Every zoom button, preset and pinch **demotes the mode to
`Custom`** — user intent beats the fit contract. `SetScaleAsync(double)` is the programmatic path.

### View modes and annotations (PD1 superseded by PDF-VIEWER-CONTINUOUS-ANNOTATIONS)

- **SinglePage** (default): one `<canvas>` plus the text layer, the search layer, the annotation
  overlay, `OverlayContent` and (since FZE-13) the templated `PageOverlayContent` for the current
  page, all aligned to the canvas wrap.
- **Continuous**: a lazy scroll column — one placeholder wrap per page inside the JS-owned
  `.tm-pdf-viewer__continuous-pages` container, painted through an IntersectionObserver + serial
  render queue (a 100+ page document never blocks the UI), canvases **released** outside the
  ±50 % viewport band (limit 12 painted pages, text/search layers cleared with them),
  `OnVisiblePageChanged` updating the two-way `Page`, the toolbar input and the active thumbnail,
  and the scroll position preserved across zoom (in-place placeholder resize), rotation and
  view-mode switches. Each page wrap is a labelled `role="group"` with `aria-current="page"`
  tracking the visible page; unpainted pages show a reduced-motion-aware shimmer.
- **Annotations in Continuous (FZE-13)**: the scroll wrap hosts a second, Blazor-owned
  `.tm-pdf-viewer__continuous-overlays` container next to the JS-owned pages DOM. `pdf-viewer.js`
  reports the page boxes of the pages inside the mount band (the same ±50 % band the paint/release
  logic uses) through the `[JSInvokable] OnContinuousPageGeometry(pages, tops, lefts, widths,
  heights, rotation)` callback — deduped by a rounded signature, fired after every build, size
  resolve chunk, in-place zoom, rotation and band change — and Blazor renders one absolutely
  positioned `.tm-pdf-viewer__page-overlay-slot` per reported page carrying the annotation layer
  (same `pdf-annotation-*` test ids, `data-page` on the slot) and the templated
  `PageOverlayContent`. Only the mounted pages ever reach Blazor's diff, so a 120-page document
  with a thread on every page keeps the lazy render and the painted-page limit intact. Z-order:
  page canvas < per-page text/search layer (z-index 1) < overlay slots (z-index 2) — markers stay
  clickable while the rest passes pointer events through to the text selection beneath.
- **Slot geometry is the page-INNER box**: the JS geometry report measures
  `.tm-pdf-viewer__page-inner` (the actual canvas box) — the outer wrap is a full-width
  centered flex row, so only its vertical extent feeds the mount band. The slots render in the
  internal `TmPdfContinuousOverlays` child with its own `SetGeometry` (the `[JSInvokable]`
  forwards to it, so the root viewer never re-renders for a geometry report); an in-place
  zoom hides the slots until the fresh report arrives, and a `ResizeObserver` on the scroll
  container re-reports when the column resizes without a scroll. The annotation layer wraps
  its content in the unrotated page plane (the same `[data-main-rotation]` transforms the
  per-page text layers use), so markers and highlights stay over their text at any rotation.
- **Selection → thread per page**: `enableSelection` gained a continuous branch — the page comes
  from the wrap containing the selection's anchor node (limited to the renderer-owned
  `.tm-pdf-viewer__continuous-pages`, so an anchor inside an overlay slot never reports the
  slot's page), the rects are normalized against that
  page's box and a selection crossing into the next page is clamped to the anchor page, so
  `OnTextSelected` (and the sticky CTA flow) create the thread on the selection's page.
- **Thread selection scrolls**: selecting a thread in Continuous no longer switches to SinglePage —
  it scrolls to the page (`scrollToPage`) and centers the anchor point (new `scrollToAnchor`
  interop, the anchor's normalized Y on the viewport's middle line); the active thumbnail and the
  two-way `Page` update through the scroll tracker's report, and the marker highlights as soon as
  the geometry report mounts its slot.
- **Single-page is the default everywhere, never a pin (FZE-14)**: the annotator's `ViewMode`
  now passes through (default `SinglePage`, additive) and `TmRedactionLayer` pins its own
  viewer explicitly (`ViewMode="SinglePage"`, no toggle — redaction measures against THE
  canvas). The non-templated
  `OverlayContent` stays single-page only — it is aligned to THE canvas; the templated
  `PageOverlayContent` (`PdfPageOverlayContext`: `PageNumber`, `IsCurrentPage`, `Width`, `Height`,
  `Rotation`) is the page-aware API and works in both modes.
- **Annotator tools per page (FZE-14)**: with `ViewMode="PdfViewMode.Continuous"` the annotator
  renders its marker layer and interaction surface per mounted page slot — ONE geometry
  pipeline (the FZE-13 slot boxes; no second page-inner measurement, no second pinch/drag —
  the shared `pointer-gesture.js` keeps owning two fingers), no per-page scroll code. The
  pointer handlers normalize against the slot box carried by `PdfPageOverlayContext`
  (`Width`/`Height`), so a stamp, comment or ink stroke always anchors on the page whose slot
  received the event; the annotator's current page is navigation state only. An ink stroke is
  confined to the page where it started (moves reported by another page's slot are ignored; a
  stroke dragged across the boundary is clipped there — `touch-action: none` stays on the Draw
  surface only, the other tools keep native pan/pinch), drafts and composers stay bound to
  their draft page across scrolling, and thread selection in the panel scrolls to the anchor
  through the shared `TmPdfViewer.ScrollToAnchorAsync(page, y)` path (mode stays Continuous).
  `ShowViewModeToggle` (default false) offers the viewer's segmented control in the merged
  toolbar; export/print are view-mode independent and cover every page.

### Text layer, search and touch

`ShowTextLayer` renders a selectable text layer (`color: transparent` — never element opacity, so
the native selection highlight and touch handles stay full strength). PDF.js v5 builds the layer
in the page's **unrotated** coordinate space (`setLayerDimensions` writes the unscaled page box
and a `data-main-rotation` attribute on the container), so rotation stays pure CSS: the package
maps the unrotated box onto the rotated canvas with `[data-main-rotation="90|180|270"]`
transforms (`transform-origin: 0 0`) — the centered single-page layer uses pre-rotation
half-box shifts, the continuous per-page layers (JS-built DOM, `::deep`) the official PDF.js
inset forms. Text selection reports
through `OnTextSelected` (`EnableTextSelectionEvents` without annotations); the report fires on
`selectionchange` + `pointerup`, and a pending selection pins the sticky comment CTA on tablet
and mobile. Search (`ShowSearch`) returns per-page matches, highlights them, and scrolls the
active match's page into view in Continuous. Touch gestures come from the core
`pointer-gesture.js`: two fingers pinch/pan (the live gesture is a CSS transform; on release the
committed scale arrives at `OnPinchZoomEnded`, clamped to 0.1–5, `ZoomMode=Custom`), one finger
scrolls (`touch-action: pan-x pan-y`), and the zoom dropdown/± buttons remain the WCAG 2.5.7
non-drag alternative. See [touch-interaction.md](touch-interaction.md).

### Thumbnails

`ShowThumbnails` renders the sidebar with a "Thumbnails" header, a collapse button and the
two-way `ThumbnailsCollapsed` (the toolbar "Pages" toggle and the sidebar button both drive it).
Thumbnails carry page numbers, the active one is highlighted (`tm-pdf-thumbnail--active`), and a
collapsed rail keeps one expand affordance. On tablet the thumbnails start collapsed; on mobile
they are a bottom sheet (`OpenThumbnailsSheetAsync()` opens it programmatically when
`ShowMobileActionBar=false`).

### Annotations (viewer side)

`EnableAnnotations` + `AnnotationProvider` (`IPdfAnnotationProvider` — in-memory when omitted)
load and persist `DocumentCommentThread`s. The panel (`TmPdfAnnotationThreadPanel`) is the docked
comments panel; `OnEditRequested` / `OnCloseRequested` drive comment editing
(`IPdfAnnotationProvider.EditAsync`) and the panel ✕ (the viewer hides the panel). `Annotations`
seeds threads without a provider; `ShowResolvedAnnotations` filters resolved ones. The panel
header carries the shared `TmPdfThreadFilter` (phase 12): the "All (N) | Unresolved (M)"
segmented radio — All ↔ `ShowResolved=true`, Unresolved ↔ `false`, live counts, native radios
(the browser owns the arrows), test ids `pdf-annotation-filter` / `…-filter-all` /
`…-filter-unresolved` — which replaced the 2.9.0 "Show resolved" checkbox in both panels and
both presentations (chrome + sheet). The Unresolved filter shows the localized
`TmPdfThread_EmptyUnresolved` state when it hides every thread. The panel width flows through
`--tm-pdf-panel-width` (viewer 280px). Selection-driven creation: select text → sticky CTA →
draft. The shared cards (`TmPdfThreadCard`, `TmPdfCommentItem`, `TmPdfComposer`,
`TmPdfThreadList`) are the same components the annotator's panel uses (PD4) and keep the
`pdf-annotation-*` test ids.

## TmPdfAnnotator

`TmPdfAnnotator` embeds a `TmPdfViewer` and renders through `TmEditorShell`. ONE toolbar: the
tool pills — Browse / Highlight / Comment / Stamp / Draw (`PdfAnnotatorMode`, two-way `Mode`;
the active tool is a tonal pill, `data-tm-group="tools"`), the color trigger (`AllowColorPicker`,
default true — FZE-12) with a visible localized label (`TmPdfAnnotator_ColorLabel`) and the
panel toggle merge into the embedded viewer's toolbar through the viewer's
`ToolbarStart`/`ToolbarEnd` slots. The color trigger (`pdf-annotator-color-trigger`) opens the
core `TmColorPalette` (`Colors` = `ColorPalette`, default `PdfAnnotationColorHelper.DefaultPalette`;
`ColorNames`, default the localized `TmPdfAnnotator_ColorName_*` names) in an overlay popover —
the F3 bottom sheet on a mobile layout, where a `pdf-annotator-color-tile` rides the G2 palette
row — plus a "Reset to author color" action (`TmPdfAnnotator_ResetColor`). The picked color is
two-way through `AnnotationColor`/`AnnotationColorChanged` and applies ONLY to NEW annotations
(stamp `CreateStampAsync`, `CreateFromDraftAsync`, the draft/ink `DraftColor`); with no pick the
color resolves per author/role via `AuthorColors` / `RoleColors`. Existing threads are never
recolored, the selection survives tool switches, and a new `Url` drops it. The export group
("Export PDF | Export flattened | Print", `data-tm-group="export"`) lives in the header row when
`ShowHeader` is true (`Title`, `Subtitle`, `HeaderStart`, `HeaderActions`); `AllowExport` /
`ExportFileName` / `ExportAsync(flatten)` / `PrintAsync()` drive it. The PD1 pin is lifted
(FZE-14): `ViewMode` (default `PdfViewMode.SinglePage`, additive — unbound hosts keep the
pre-FZE-14 presentation) and `ViewModeChanged` pass through two-way, `ShowViewModeToggle`
(default false) offers the viewer's segmented control in the merged toolbar, and Continuous
renders the layer + surface per page slot (see the viewer's
"Annotator tools per page" above); `ShowViewerToolbar=false`
(compatibility) hides the viewer chrome and renders a compact tools row instead.

Pass-through parameters: `ShowThumbnails`, `ShowSearch`, `AllowRotation`,
`ThumbnailsCollapsed` (two-way, unbound-host safe), `ZoomMode` (two-way), `ShowOpenLink`,
`ToolbarLabels`, `ViewMode`/`ViewModeChanged` (two-way, FZE-14) and `ShowViewModeToggle`
(FZE-14). `ShowPanel` / `ShowPanelChanged` two-way drive the thread panel
(`TmPdfAnnotatorPanel`): the ✕ raises `ShowPanelChanged` even without a binding, and the toolbar
toggle reopens it. `Height` (default "600px") is owned by the annotator root; the viewer flexes
inside it.

Responsive (`LayoutMode`, default `Auto`):

- **Desktop** — merged toolbar + docked thread panel (with a resize separator).
- **Tablet** — the tools render as a **full-width labelled row** above the canvas (never inside
  the narrow viewer toolbar overflow). The row is a **single scrollable line** at every
  container width: when the docked panel squeezes the canvas the row scrolls horizontally
  (like the mobile tile row) instead of wrapping or dropping labels — the tool labels, the
  colour chip label and the trailing panel toggle (sticky right) all stay visible. The panel
  is a docked aside and the thumbnails reuse the viewer's guarded default (collapsed).
- **Mobile — G2: viewing + light edits only.** The tool palette is the tile row with exactly
  *Browse* and *Add comment* (highlight/stamp/draw are hidden; an active edit mode falls back to
  Browse with a localized hint `TmPdfAnnotator_EditOnLargerScreen`), the F5 `TmMobileActionBar`
  carries "Export PDF | Export flattened | Print" with the threads entry in the shared "More"
  menu, and the thread panel is a non-modal bottom sheet (peek/half/full) that starts **CLOSED**
  at load (and on every desktop/tablet→mobile switch unless the host explicitly supplies
  `ShowPanel`). A sheet opened from a marker or a draft lands on the HALF snap with the composer
  pinned to the sheet bottom; closing returns focus to the opener via
  `tmPdfAnnotator.rememberFocus`/`focusOpener`. Hosts needing the full tool set on a phone pin
  `LayoutMode="TmLayoutMode.Desktop"`.

Touch: in Draw mode one finger or the pen draws and two fingers hand over to the pinch (the
in-progress stroke is cancelled on the second finger; a `pen` landing replaces a live finger
stroke — the pen has priority; a palm touch during a pen stroke is ignored). `touch-action: none`
claims only the Draw modifier — Comment/Stamp surfaces keep native pan/pinch. A tablet-only
sticky "Highlight" CTA (`TmPdfAnnotator_HighlightSelection`) surfaces a touch-selection draft
when the panel is closed or out of reach.

## Wireframe stencils

The `TmPdfViewer` stencil (role `pdf-viewer`) mirrors the redesigned chrome with the props
`showThumbnails`, `viewMode` (`single` | `continuous`), `showHeader`, `title` and `zoomMode`
(`auto` default | `custom` | `fit-width` | `fit-page`); the `TmPdfAnnotator` stencil (role
`pdf-annotator`) carries `showThumbnails`, `showSearch`, `showHeader`, `viewMode`
(`single` default | `continuous` — the continuous variant draws a scroll column of per-page
annotator surfaces, FZE-14) and `title` on top of
`mode` / `showPanel` / `allowExport`. The static stencils keep the roles; hosts key off the
props, not the stencil internals.

## Localization keys

All keys live in en/cs/sk/fr and the `MockTmLocalizer`. The families hosts most often reword:
`TmPdfViewer_PageOfTotal` (the "N of M" template, also the continuous page-wrap label),
`TmPdfViewer_CommentsCount` (a `Loc.Plural` family), `TmPdfThread_ReplyCount` (a `Loc.Plural`
family — "{0} reply"/"{0} replies" / cs "{0} odpověď"/"{0} odpovědi"/"{0} odpovědí"),
`TmPdfThread_FilterAll` / `TmPdfThread_FilterUnresolved` (the phase-12 filter's `Loc.Plural`
families, "All ({0})"/"Unresolved ({0})" — English ships `_One`/`_Other`, cs/sk all four forms),
`TmPdfViewer_PageAnnotations` (the per-page overlay slot's group label, "Annotations on page {0}"),
`TmPdfAnnotator_ColorLabel`,
`TmPdfAnnotator_ColorName_*` (the default palette's localized swatch names),
`TmPdfAnnotator_ChooseColor` / `TmPdfAnnotator_ResetColor`, `TmPdfAnnotator_TogglePanel`,
`TmPdfAnnotator_EditOnLargerScreen`, `TmPdfViewer_SelectionPrompt` / `TmPdfViewer_DismissSelection`,
`TmPdfAnnotator_HighlightSelection`. Counted texts must provide the plural forms each language's
grammar needs — [localization-plurals.md](localization-plurals.md). Timestamps render RELATIVE
through the shared `ITmRelativeTimeFormatter` with the absolute instant in `<time datetime>` and
`title`. The 2.9.0 `TmPdfViewer_ShowResolved` / `TmPdfAnnotator_ShowResolved` keys were removed
with the phase-12 filter (see the CHANGELOG migration); the 2.9.0
`TmPdfViewer_AnnotationsSinglePageOnly` hint key followed when FZE-13 superseded PD1.

## Theming

The package reads canonical core tokens only (`--tm-color-primary`, `--tm-border-color`,
`--tm-color-surface-elevated`, `--tm-color-surface-hover`, `--tm-text-primary/-secondary`,
`--tm-color-on-primary`, `--tm-radius-card/-control`, `--tm-color-warning`, `--tm-bg-workspace`,
`--tm-shadow-card`, `--tm-focus-ring`), so the palette, the indigo theme and dark mode propagate
without any package-local dark blocks. Component-local knobs are defined on each component root,
never globally: `--tm-pdf-page-shadow` and `--tm-pdf-panel-width` (viewer thumbnails 140px,
annotator panel 300px, viewer thread panel 280px). The annotation data palette (per-author
colors) is allow-listed. Override canonical tokens for all three theme selectors at once — see
[css-token-aliases.md](css-token-aliases.md).

## Migration (2.9.0 → 2.9.1)

Each item ships a before/after example in the CHANGELOG's **Breaking / Migration** section; the
summary:

1. **Tokens** — the PDF components no longer read the undefined `--tm-primary`, `--tm-border`,
   `--tm-hover`, `--tm-text`, `--tm-surface-elevated`, `--tm-radius`, `--tm-warning`,
   `--tm-bg-subtle` aliases or any `--tm-*-dark` override; they read the canonical tokens.
   Re-skin by overriding the canonical tokens on `:root`, `[data-theme="dark"]` **and** `.tm-dark`.
2. **Viewer toolbar DOM** — the hand-rolled row is the core `TmToolbar`: `.tm-pdf-viewer__group`
   → `.tm-toolbar-group`, `.tm-pdf-viewer__toolbar-trailing` → the toolbar's
   `.tm-toolbar-actions`, `.tm-pdf-viewer__separator` → `.tm-toolbar-divider`; group wrappers
   carry `data-tm-group`. "Download" is the primary action, "Open" the secondary.
3. **`ZoomMode` default is `Auto`** — fit-width on a mobile layout. To restore the 2.9.0
   behavior at every width, set `ZoomMode="PdfZoomMode.Custom"`.
4. **Annotator toolbar** — the two mismatched bars are one: the tool group merges into the
   embedded viewer's toolbar via `ToolbarStart`/`ToolbarEnd`, the export group lives in the
   header row (`ShowHeader`), mode pills are tonal, and `TmPdfAnnotator_PanelToggle` →
   `TmPdfAnnotator_TogglePanel` / `TmPdfAnnotator_YourColor` → `TmPdfAnnotator_ColorLabel` were
   renamed. Every `pdf-annotator-*` test id survives.
5. **Annotator layout** — `.tm-pdf-annotator__body` is gone; the viewer lives in
   `[data-region='canvas']` and the panel in `[data-region='right']` of the `TmEditorShell`. On
   mobile the sheet starts CLOSED — a host that wants it open supplies `ShowPanel="true"`.
6. **Panel internals** — both panels' item/thread DOM is the shared `TmPdfThreadCard` +
   `TmPdfCommentItem` + `TmPdfComposer` (+ `TmPdfThreadList`); the old
   `tm-pdf-annotation-*`/`tm-pdf-annotator-panel-*` item classes are replaced by the
   `tm-pdf-thread-*`/`tm-pdf-comment-*`/`tm-pdf-composer-*` classes (the panels keep their
   public parameters and `data-testid` contract).
7. **Panels on tablet/mobile** — the comments panel is a side sheet (tablet) or a bottom sheet
   (mobile) and **closed by default** there; the thumbnails start collapsed on tablet. Hosts
   relying on open-at-load panels on narrow containers should open them themselves or pin
   `LayoutMode="TmLayoutMode.Desktop"`.
8. **Time format & plurals** — card/comment timestamps are relative text with the absolute
   instant in `<time datetime>`/`title`; counted texts are `Loc.Plural` families
   (`TmPdfThread_ReplyCount`, `TmPdfViewer_CommentsCount`, `TmPdfAnnotator_DraftStrokes`).
   Hosts overriding those keys must ship the plural forms.
9. **Continuous** — annotations render per mounted page (FZE-13, PD1 superseded); the
   non-templated `OverlayContent` stays single-page only, and the selection report resolves the
   anchor page so threads land on the page the text was selected on.
10. **Search presentation** — `SearchPresentation` defaults to **`Floating`**: on desktop/tablet
   the search UI is a card anchored over the page body (focus lands in its input; Escape
   restores the toolbar focus; a zero-match search renders `TmPdfViewer_SearchNoMatches`).
   Hosts that want the pre-2.9.1 full-width bar set `SearchPresentation="PdfSearchPresentation.Inline"`.
11. **Rotate tooltips** — the single `TmPdfViewer_Rotate` label became a direction pair:
   `TmPdfViewer_RotateCounterClockwise` + `TmPdfViewer_RotateClockwise`. Hosts overriding
   resource keys must ship both (the desktop/tablet rotate group is icon-only — the full text
   rides the tooltip and aria-label).
12. **Thread filter & color picker (phase 12)** — the panels' "Show resolved" checkbox is the
    shared `TmPdfThreadFilter` (`pdf-annotation-filter`/`pdf-annotator-filter` with
    `-all`/`-unresolved` segments; `TmPdfViewer_ShowResolved`/`TmPdfAnnotator_ShowResolved` keys
    removed), and the annotator's color chip is the `pdf-annotator-color-trigger` button opening
    the palette (mobile: the `pdf-annotator-color-tile` sheet). `AllowColorPicker="false"`
    restores the static chip + label pair.

## Testing

- **bUnit** — `tests/Tempo.Blazor.Tests/Components/Files/` (viewer, annotator, panels, cards)
  plus `Components/NotionEditor/TmNotionPdfBlockTests.cs`; the CSS token contract lives in
  `Theme/PdfViewerCssTokenTests.cs`. Forced `LayoutMode` values render a mode without a DOM
  measurement.
- **Node lanes** — `npm run test:pdf-viewer` (size/render/fit/continuous/download/gesture/
  selection/init/generations/classes modules); `scripts/run-node-tests.mjs` enumerates files
  explicitly, no globs.
- **E2E** — `tests/Tempo.Blazor.E2E` `PdfViewer*` / `PdfAnnotator*` / `Redaction*` suites
  (desktop 1440, tablet 1180×820 touch — the forced-tablet sections render full-bleed so the
  evidence container is ≥768px wide, mobile 390×844 and 320×640); screenshots land in
  `tests/Tempo.Blazor.E2E/__screenshots__/pdf-viewer/`, `…/pdf-annotator/` and
  `…/redaction/`. The demo loads a
  local 120-page fixture (`_content/Tempo.Blazor.Demo.SharedUI/sample-data/tempo-pdf-viewer-120p.pdf`).
- **Audits** — the strict CSS token audit (`scripts/audit-css-strict.mjs`, baseline 0 for the
  package), `ScopedCssOwnershipTests` and `ComponentDocumentationFreshnessTests` (regenerate the
  JSON bundles after touching the `[Parameter]` surface).

## Reference deviations

The redesign references live in `output/imagegen/tempo-reimagined/Tempo.Blazor.PdfViewer/`;
each is reviewed against the E2E screenshots under
`tests/Tempo.Blazor.E2E/__screenshots__/`. Statuses: **matched** (the reference presentation is
implemented and screenshotted), **intentional** (a documented deviation from the reference) and
**backlog** (reference content deliberately not built).

| Reference | E2E screenshot(s) | Status |
|-----------|-------------------|--------|
| `pdf-viewer--desktop.png` | `pdf-viewer/pdf-viewer--{light,dark}-{default,indigo}--desktop.png`, `toolbar-desktop.png`, `header-desktop.png` | **matched** — one toolbar row with grouped pill labels, editable page input, zoom presets, Download primary / Open secondary; the theme shots also prove token propagation |
| `pdf-viewer--tablet.png` | `pdf-viewer/tablet-toolbar.png`, `tablet-comments-sheet.png` | **matched** — captured at 1180×820 in a ≥768px full-bleed container; 44px coarse targets, labels below icons, thumbnails collapsed |
| `pdf-viewer--mobile.png` | `pdf-viewer/mobile-320-all-features.png`, `mobile-fit-width.png`, `mobile-more-menu.png` | **matched** — compact single row, "More" overflow, fit-width Auto, action bar; shot at 320 (the smallest supported phone) instead of the mock's 390 |
| `pdf-viewer-continuous--desktop.png` | `pdf-viewer/continuous-desktop.png`, `continuous-start.png` | **matched** — lazy scroll column, painted page cards, scroll-tracked page number |
| `pdf-viewer-continuous--tablet.png` | `pdf-viewer/mobile-continuous.png` (chrome is forced-tablet-identical except the label position) | **intentional** — no separate tablet continuous capture; the layout is container-driven and the tablet chrome is covered by `tablet-toolbar.png` |
| `pdf-viewer-continuous--mobile.png` | `pdf-viewer/mobile-continuous.png` | **matched** — per-page text layer + search highlights in the scroll column |
| `pdf-annotator--desktop.png` | `pdf-annotator/pdf-annotator--{light,dark}-{default,indigo}--desktop.png`, `stamp-placed.png` | **matched** — one merged toolbar (tool pills, colour label, panel toggle), header export row |
| `pdf-annotator--tablet.png` | `pdf-annotator/tablet-tools-labeled.png` | **matched** — full-width labelled tools row (single scrollable line), docked panel; ≥768px container since phase 10 |
| `pdf-annotator--mobile.png` | `pdf-annotator/mobile-default.png`, `mobile-320-annotator.png` | **intentional (G2)** — the mock keeps the full palette on a phone; the implementation is viewing + light edits only (Browse + Add comment, export/action bar) |
| `pdf-annotator-panel--desktop.png` | `pdf-annotator/panel-cards.png`, `two-users-thread-shared.png` | **matched** — docked aside with the shared compact thread cards |
| `pdf-annotator-panel--tablet.png` | `pdf-annotator/tablet-drawing.png` | **matched** — docked aside hosting the composer while drawing |
| `pdf-annotator-panel--mobile.png` | `pdf-annotator/mobile-reply-sheet.png`, `mobile-edit-comment.png` | **matched** — non-modal bottom sheet (peek/half/full), closed at load |
| `pdf-annotation-thread-panel--desktop.png` | `pdf-viewer/annotations-panel.png`, `annotations-detail.png` | **matched** — docked comments panel with the shared cards |
| `pdf-annotation-thread-panel--tablet.png` | `pdf-viewer/tablet-comments-sheet.png` | **intentional** — the reference shows the side sheet open; the implementation starts it **closed** (open through the toolbar toggle), restoring focus to the toggle |
| `pdf-annotation-thread-panel--mobile.png` | `pdf-viewer/mobile-comments-sheet.png` | **matched** — bottom sheet with the compact cards |

Cross-cutting deviations that apply to several references:

- **App shell** — the mockups paint a standalone application (logo, breadcrumbs, back arrow,
  status bar, global search in the header). The library ships components, not an app: hosts
  own the shell, the demo pages embed the components in their own chrome. *(intentional)*
- **Icon-only labels by default** — the references show labelled toolbars everywhere; the
  shipped default is `ToolbarLabels.Icons` (labels via `Auto`/`IconsWithText`, or below the
  icons on a tablet layout). The comments count keeps its text regardless. *(intentional)*
- **Compact thread cards** — the shared `TmPdfThreadCard`/`TmPdfCommentItem` cards are denser
  than the mockup's cards (relative timestamps, smaller paddings) so both panels fit narrow
  containers; the mockup's per-layout card variants were unified into one. *(intentional)*
- **Invented features (not built)** — per the analysis `[E]` lists: the "Tempo" app shell,
  "Open in app" wording ("Open" kept), file-size metadata in the header, the global search
  field in the annotator header, hand/pan + text-annotation + undo/redo tools, the unbound
  bottom composer, "All/Unresolved" tabs (the "Show resolved" toggle ships instead),
  comment popovers tethered to markers, document-structure references ("Article 1"), photo
  avatars (initials only) and a second (counter-clockwise) rotation. *(backlog — none are
  planned; hosts may add their own via the slots)*
