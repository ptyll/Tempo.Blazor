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
| `TmPdfAnnotationLayer` / `TmPdfAnnotatorLayer` | Page-overlay marker layers (single-page only, PD1) |
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
  `TmMobileActionBar` ("Pages | Hledat | Komentáře (N)" — only enabled features render tiles),
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
| `rotate` | Rotate button (`AllowRotation`) |
| `view` | The single/continuous segmented control (`ShowViewModeToggle`) |
| `panels` | The "Pages" thumbnails toggle |
| `search` | The search toggle (`ShowSearch`) |
| `comments` | The comments toggle with the live open-thread count ("Comments (N)") |
| `export` | The trailing file links (Download primary, Open secondary) + host `ToolbarEnd` |

The annotator adds its `tools` group and its panel toggle into the same row. Where a group
renders (toolbar / "More" menu / mobile row / header) is a layout detail — hosts key off
`data-tm-group` and the test ids (`pdf-page-count`, `pdf-search-count`, `pdf-viewer-toolbar`,
…), never positional selectors. `ToolbarLabels` (`Icons` default, `Auto`, `IconsWithText`)
controls the visible labels; on a tablet layout they render below the icons.

### Header and slots

`ShowHeader="true"` renders a document header above the toolbar: `Title`, a subtitle
(`Subtitle`, defaulting to a neutral "Pages: N" once loaded), the `HeaderStart` slot, and the
`HeaderActions` slot after the **Download (primary)** and **Open (secondary)** links — with the
header on, those two move out of the toolbar. `ToolbarStart` / `ToolbarEnd` compose host groups
into the toolbar (this is how `TmPdfAnnotator` merges its tools), and
`SelectionCtaContent` fills the sticky in-flow selection CTA row that sits **outside** the scroll
area (a zoom or scroll never carries it away).

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

### View modes and PD1

- **SinglePage** (default): one `<canvas>` plus the text layer, the search layer, the annotation
  overlay and `OverlayContent`, all aligned to the canvas wrap.
- **Continuous**: a lazy scroll column — one placeholder wrap per page with the real aspect
  ratio, painted through an IntersectionObserver + serial render queue (a 100+ page document
  never blocks the UI), canvases **released** outside the ±50 % viewport band (limit 12 painted
  pages, text/search layers cleared with them), `OnVisiblePageChanged` updating the two-way
  `Page`, the toolbar input and the active thumbnail, and the scroll position preserved across
  zoom (in-place placeholder resize), rotation and view-mode switches. Each page wrap is a
  labelled `role="group"` with `aria-current="page"` tracking the visible page; unpainted pages
  show a reduced-motion-aware shimmer.
- **PD1 — annotations are single-page only**: in Continuous the annotation overlay and
  `OverlayContent` do not render, and selection-driven annotation creation is off. The thread
  panel stays readable with a localized hint (`TmPdfViewer_AnnotationsSinglePageOnly`), and
  selecting a thread switches the viewer back to `SinglePage` on the thread's page.
  `TmPdfAnnotator` and `TmRedactionLayer` pin SinglePage permanently.

### Text layer, search and touch

`ShowTextLayer` renders a selectable text layer (`color: transparent` — never element opacity, so
the native selection highlight and touch handles stay full strength). Text selection reports
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
width flows through `--tm-pdf-panel-width` (viewer 280px). Selection-driven creation: select
text → sticky CTA → draft. The shared cards (`TmPdfThreadCard`, `TmPdfCommentItem`,
`TmPdfComposer`, `TmPdfThreadList`) are the same components the annotator's panel uses (PD4) and
keep the `pdf-annotation-*` test ids.

## TmPdfAnnotator

`TmPdfAnnotator` embeds a `TmPdfViewer` and renders through `TmEditorShell`. ONE toolbar: the
tool pills — Browse / Highlight / Comment / Stamp / Draw (`PdfAnnotatorMode`, two-way `Mode`;
the active tool is a tonal pill, `data-tm-group="tools"`), the color indicator with a visible
localized label (`TmPdfAnnotator_ColorLabel`; `AnnotationColor` explicit, else per-author/role
via `AuthorColors` / `RoleColors`), and the panel toggle merge into the embedded viewer's toolbar
through the viewer's `ToolbarStart`/`ToolbarEnd` slots. The export group ("Export PDF | Export
flattened | Print", `data-tm-group="export"`) lives in the header row when `ShowHeader` is true
(`Title`, `Subtitle`, `HeaderStart`, `HeaderActions`); `AllowExport` / `ExportFileName` /
`ExportAsync(flatten)` / `PrintAsync()` drive it. The annotator pins the viewer to `SinglePage`
(PD1) and never offers the continuous toggle; `ShowViewerToolbar=false` (compatibility) hides
the viewer chrome and renders a compact tools row instead.

Pass-through parameters: `ShowThumbnails`, `ShowSearch`, `AllowRotation`,
`ThumbnailsCollapsed` (two-way, unbound-host safe), `ZoomMode` (two-way), `ShowOpenLink` and
`ToolbarLabels`. `ShowPanel` / `ShowPanelChanged` two-way drive the thread panel
(`TmPdfAnnotatorPanel`): the ✕ raises `ShowPanelChanged` even without a binding, and the toolbar
toggle reopens it. `Height` (default "600px") is owned by the annotator root; the viewer flexes
inside it.

Responsive (`LayoutMode`, default `Auto`):

- **Desktop** — merged toolbar + docked thread panel (with a resize separator).
- **Tablet** — the tools render as a **full-width labelled row** above the canvas (never inside
  the narrow viewer toolbar overflow; the colour chip label stays visible), the panel is a
  docked aside and the thumbnails reuse the viewer's guarded default (collapsed).
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
`pdf-annotator`) carries `showThumbnails`, `showSearch`, `showHeader` and `title` on top of
`mode` / `showPanel` / `allowExport`. The static stencils keep the roles; hosts key off the
props, not the stencil internals.

## Localization keys

All keys live in en/cs/sk/fr and the `MockTmLocalizer`. The families hosts most often reword:
`TmPdfViewer_PageOfTotal` (the "N of M" template, also the continuous page-wrap label),
`TmPdfViewer_CommentsCount` (a `Loc.Plural` family), `TmPdfThread_ReplyCount` (a `Loc.Plural`
family — "{0} reply"/"{0} replies" / cs "{0} odpověď"/"{0} odpovědi"/"{0} odpovědí"),
`TmPdfViewer_AnnotationsSinglePageOnly`, `TmPdfAnnotator_ColorLabel`, `TmPdfAnnotator_TogglePanel`,
`TmPdfAnnotator_EditOnLargerScreen`, `TmPdfViewer_SelectionPrompt` / `TmPdfViewer_DismissSelection`,
`TmPdfAnnotator_HighlightSelection`. Counted texts must provide the plural forms each language's
grammar needs — [localization-plurals.md](localization-plurals.md). Timestamps render RELATIVE
through the shared `ITmRelativeTimeFormatter` with the absolute instant in `<time datetime>` and
`title`.

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
9. **Continuous** — annotations and `OverlayContent` are single-page only (PD1); text-selection
   annotation creation is disabled in Continuous.

## Testing

- **bUnit** — `tests/Tempo.Blazor.Tests/Components/Files/` (viewer, annotator, panels, cards)
  plus `Components/NotionEditor/TmNotionPdfBlockTests.cs`; the CSS token contract lives in
  `Theme/PdfViewerCssTokenTests.cs`. Forced `LayoutMode` values render a mode without a DOM
  measurement.
- **Node lanes** — `npm run test:pdf-viewer` (size/render/fit/continuous/download/gesture/
  selection/init/generations/classes modules); `scripts/run-node-tests.mjs` enumerates files
  explicitly, no globs.
- **E2E** — `tests/Tempo.Blazor.E2E` `PdfViewer*` / `PdfAnnotator*` / `Redaction*` suites
  (desktop 1440, tablet 1024×768 touch, mobile 390×844); screenshots land in
  `tests/Tempo.Blazor.E2E/screenshots/pdf-viewer/` and `…/pdf-annotator/`. The demo loads a
  local 120-page fixture (`_content/Tempo.Blazor.Demo.SharedUI/sample-data/tempo-pdf-viewer-120p.pdf`).
- **Audits** — the strict CSS token audit (`scripts/audit-css-strict.mjs`, baseline 0 for the
  package), `ScopedCssOwnershipTests` and `ComponentDocumentationFreshnessTests` (regenerate the
  JSON bundles after touching the `[Parameter]` surface).
