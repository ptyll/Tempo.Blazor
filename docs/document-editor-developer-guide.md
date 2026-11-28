# TmDocumentEditor Developer Guide

Tento dokument shrnuje integrační body, které vznikly během CKEditor-inspired redesignu `TmDocumentEditor`. Praktické demo je na route `/document-editor`; ukazuje toolbar režimy, feature toggles, image provider, table properties, comments/review, paste report a autosave error scénář.

> **Redesign map (plan 146c7e1b, closed in phase 9).** The chrome of the editor was rebuilt in nine phases; this guide is the contract, the
> topic documents are [document-editor-tablet-mobile.md](document-editor-tablet-mobile.md) (G2, sheets, action bar), [toolbar.md](toolbar.md)
> (the ribbon on the core `TmToolbar`) and the core docs for the primitives the editor only wraps (`TmOverlayPanel`, `TmModal`, `TmCommandPalette`,
> `TmMobileActionBar`, `TmLayoutObserver`). Jump to: [Layout Modes](#layout-modes), [Header and host slots](#header-and-host-slots-g9),
> [Contextual inspector and find](#contextual-inspector-and-find-de2), [Document-region modes](#document-region-modes-and-the-comparison),
> [Phone scope](#phone-scope-decision-g2), [The `data-priority` contract](#the-data-priority-contract), [What is not here](#what-the-redesign-did-not-do).

## Feature Registry

Editor startuje s `DocumentEditorBuiltInFeatures.CreateDefaultRegistry()`. Feature implementuje `IDocumentEditorFeature` a má stabilní `Name`, volitelné `Requires` a registrační metody pro commandy, toolbar, shortcuty, floating UI a schema hooky.

Host aplikace může pro konkrétní instanci vypnout features přes `DisabledFeatures`. Vestavěné názvy jsou v `DocumentEditorFeatureNames`, například `image`, `table`, `comments` a `trackChanges`. Vypnutí feature má skrýt její toolbar položky a zablokovat související editor flow, ne pouze změnit vzhled tlačítka.

## Command Registry

Commandy žijí v `DocumentEditorCommandRegistry` a mají metadata pro label, tooltip, kategorii, shortcut, ikonu, viditelnost a disabled reason. Všechny cesty spuštění, tedy toolbar, command palette, keyboard shortcuty, overflow menu a contextual UI, mají používat stejný registry-backed stav.

Nový command má mít stabilní název, lokalizované texty v resources, guard přes `computeEnabled` nebo force-disable reason a test, který ověří, že disabled command nejde spustit ani mimo viditelné UI.

## Toolbar Modes

`ToolbarMode` podporuje `Ribbon`, `Compact` a `DistractionFree`. Ribbon je výchozí Word-like režim. Compact zachovává command coverage, ale preferuje icon-only controls s aria labely. Distraction-free toolbar skryje a ponechá plochu editoru jako primární UI.

Demo route obsahuje `document-editor-toolbar-mode`, aby host aplikace viděl runtime přepnutí bez reloadu dokumentu.

### The ribbon row (DE-1.2)

The ribbon is ONE icon row on the core `TmToolbar` (`Overflow=Menu`, `OverflowPresentation=Popover`); see [toolbar.md](toolbar.md#migrating-from-the-documenteditor-toolbar-de-12).

- **Contract for hosts and tests:** every ribbon command keeps `data-command` and `data-testid` and publishes `data-priority`
  (`primary` / `secondary` / `overflowonly`) - the rank declared in `DocumentEditorBuiltInToolbar` (`DefaultItems` for the
  registered commands, `RibbonLocalCommands` for the popup/toolbar-local ones). Hosts never key off the group DOM.
- **Labels:** groups have no printed caption (their name is the `aria-label` and the section header in More). Icon buttons carry a
  tooltip and an accessible name; only Link, Comment, Image and Table show a label (`ToolbarMode.Ribbon`, container wide enough:
  below a desktop container they are icon-only too). `Compact` is icon-only throughout.
- **Overflow:** which commands fit is measured by `tm-toolbar.js`; `Secondary` leaves before `Primary`, `OverflowOnly` never renders on
  the bar. The More menu is the core `role="menu"` (groups, arrows, typeahead; toggles are `menuitemcheckbox`). Every item that owns a
  popup has a More form too (phase 5), so nothing is pinned. A test or host that needs a command that may be collapsed clicks the bar button
  when it is visible and the More entry otherwise (`DocumentEditorRedesignE2EHelpers.ClickRibbonAsync` is the reference helper).
- **Phone:** `TmDocumentEditorToolbar.PhoneLayout` renders Home, Insert, Review and View as ONE tools row (the core overflow, "More" is a sheet);
  On a phone B I U, alignment and the bulleted list stay `Primary` and every other command is `Secondary` (a 320 px row is B I U + alignment, a 390 px row adds the list; the row's gutter is 2 px); the published `data-priority` stays the registry rank.

### Floating UI (DE-1.4 .. DE-1.7)

Every floating editing surface is a `TmDocumentFloatingPanel` - a thin wrapper over the core `TmOverlayPanel` (no sheet and no focus trap of
its own): the ribbon panels (style gallery, table grid, equation, symbol, page layout, header/footer fields and presets, link), the image
panel, the token panel and the form-field popover (`TmDocumentFieldPopover`: content controls and signing fields).

- **Anchoring and dismissal.** The panel is a top-layer popover anchored to the trigger. An outside press closes it and is NOT swallowed
  (a click on the header Undo still undoes; a canvas click still places the caret); Escape closes it and `OnRestoreFocus` gives the focus
  back to the canvas. There is no scrim for a menu on a desktop layout; a mobile layout presents the same content as the core bottom sheet.
- **Where it renders.** The ribbon panels render AFTER the toolbar's tabpanel (never inside a `TmToolbarItem`, whose collapsed state is
  invisible and inert), anchored to the trigger button or - when the request came from the More menu - to the ribbon row. The image,
  token and field panels render in `document-floating-root`. The modals (version, compare, debug) are core `TmModal`s and
  render as siblings of the floating root and of the `.tm-document-editor__main` size container; the command palette is the core `TmCommandPalette`
  (see "Command palette" below). A forced `LayoutMode` is cascaded as the viewport scope to all of them, so a forced Mobile editor presents sheets
  and fullscreen dialogs.
- **Selection guard.** Pointer and click inside a panel are fenced (`stopPropagation`) and the triggers `preventDefault` the pointer press, so
  the canvas selection survives; every choice applies immediately (G8) - the only footers are a "Done" on the page layout, a "Close" on the
  field popover and Cancel / Insert on the image panel.
- **Contract for hosts and tests:** panels carry `data-testid` (unchanged from the inline popups) and `data-floating-kind`; choices are
  `DocumentPaletteGrid` buttons with a roving tab stop (arrow keys follow the columns - `SheetColumns` in a bottom sheet). Select a panel by its
  test id, never by its position inside the ribbon. `DocumentPaletteGrid<TItem>` is public and generic on purpose: it is a candidate to move
  into the core package once a second host needs a titled grid of choices.
- **Close reasons.** `OnClose(DocumentFloatingCloseReason)`: `Button` (X / footer Close), `Escape`, `Outside` (a press elsewhere - the host must NOT take
  the focus back) and `Done` (the bottom sheet's confirm: a form panel applies what is valid, a panel that applies at once treats it like Button).
  A host numbers every open of a panel and ignores a report from an earlier open (the core overlay reports an outside press asynchronously).
- **Field popover** anchors to a VIEWPORT rectangle (`CanvasEngineSelectionState.FieldAnchor`, scaled by the page zoom): it closes when the canvas
  scrolls, the window resizes or the zoom changes, and the next selection sync reopens it at the new rectangle.
- **Command palette.** `TmDocumentCommandPalette` is an adapter: `DocumentEditorCommandState` becomes `ICommandPaletteAction` (title = label,
  `IsDisabled` / `DisabledReason`, `SearchTerms` = name + category, localized category) and the core `TmCommandPalette` renders it
  (`GroupByCategory`, `SplitShortcuts`, `ItemAttributes` for the `document-command-palette-item` / `data-command` hooks). Do not restyle
  `.tm-command-palette-*` in the editor - add to the `.tm-document-command-palette` modifier only.

## Clipboard Pipeline Extension Point

Paste flow prochází přes `DocumentClipboardPipeline` a normalizéry pro Word, Google Docs, Google Sheets, raw HTML a URL. Normalizér vrací bloky, warnings a debug snapshot. Warnings se uživateli zobrazují přes `TmDocumentPasteReport`.

Při přidávání normalizéru preferuj schema-aware výstup, zachovej undo jako jednu transakci a přidej paste fixture do `tests/Tempo.Blazor.Tests/Fixtures/DocumentEditor/Clipboard`.

## Image Provider UX

`ImageProvider` řeší upload a clipboard image flow. `ImageUrlResolver` řeší render provider-managed assetů. Když `ImageProvider` není nastavený, upload volba musí být disabled a paste report má vysvětlit, že obrázek nebylo možné uložit.

Demo route má toggle `document-editor-image-provider-enabled`, který ukazuje rozdíl mezi URL insertion a provider-backed uploadem.

## Table Properties Model

Tabulky používají `TableBlockContent`, `TableLayoutContent`, `TableRowContent` a `TableCellContent`. Table-level nastavení zahrnuje šířku, zarovnání, default padding, background a borders. Cell-level nastavení zahrnuje header flag, spans, merge metadata, vertical alignment, padding, background a borders.

Floating table toolbar otevírá `TmDocumentTablePropertiesPanel` a `TmDocumentCellPropertiesPanel`. Demo dokument `table-demo` obsahuje předpřipravenou tabulku pro manuální kontrolu těchto panelů.

## Autosave And Pending Actions

Autosave řídí `DocumentAutosaveStateMachine` a pending stav se promítá do status baru. UI musí rozlišit waiting, saving, saved, recoverable error a retry. `DocumentPendingActionService` drží další blokující operace, například image upload.

Demo toggle `document-editor-autosave-error` přepne provider do recoverable failure režimu a zkrátí autosave interval, aby šlo chování rychle ověřit.

## Watchdog Recovery

JS-owned WYSIWYG runtime má recovery/watchdog flow pro výpadky runtime bridge. Komponenta má při runtime problému zobrazit fallback místo prázdné plochy a debug modal má obsahovat recovery detail pro diagnostiku.

Při úpravách JS runtime vždy spusť `node --check src/Tempo.Blazor/wwwroot/js/document-editor-wysiwyg.js` a cílené runtime testy.

## Theming, Highlight Tokens And Panel Building Blocks

The editor chrome paints through the core semantic tokens only (`--tm-color-surface`, `--tm-bg-workspace`,
`--tm-radius-card` / `--tm-radius-control`, `--tm-shadow-card` / `--tm-shadow-popover`); dark mode and the opt-in indigo
theme (`data-tm-theme="indigo"`) ride the token layer, so the stylesheets carry no `[data-theme="dark"]` block, no
colour literal and no decorative gradient. To re-brand the editor override tokens on an ancestor, never a selector.

`.tm-document-editor` maps five highlight tokens onto the core tokens: `--tm-document-highlight-match`,
`-match-active` (search), `-insert`, `-delete` and `-change` (revision overlays). The canvas search overlay sets only the
geometry inline; the colours live in `_document-editor-find.css`, and the revision / comment markers in the global `_document-editor-canvas-overlays.css` (the engine owns that DOM; a scoped `::deep(.x)` rule would compile to an invalid selector - write `.host ::deep .x` or use a global stylesheet). The canvas drawing (text, tables, shapes) is
never styled by these tokens.

Shared panel building blocks (stylesheet `_document-editor-panels.css`, imported by
`tempo-blazor-document-editor.css`):

- `TmDocumentInspectorHeader` - icon, title heading (its id is what the panel region labels itself by through
  `aria-labelledby`), subtitle, host actions slot and a close button that appears only when `OnClose` is bound.
- `.tm-document-inspector-form` - the form pattern: `__section` + `__section-toggle` (`aria-expanded`) with a
  `chevron-down` `__chevron` icon, `__section-body`, `__field` (label beside the control while the panel body is wide
  enough, wrapping above it when it is not - an intrinsic flex-wrap on the body's own width, no container query, not the
  viewport; `--stacked` keeps the label above at any width), `__suffix` for units and `__segmented` for icon or
  icon + text choices. `--embedded` drops the frame and the shadow for a panel hosted inside the inspector. Keep the
  label wrapping its control (implicit association) or use `for` / `aria-labelledby`.
- `TmDocumentInspectorHeader.OnBack` renders a back arrow before the title (the way from a contextual inspector to the tab
  bar); `TmDocumentInspectorSection` is the foldable section of the form pattern (`aria-expanded` toggle, the body stays in
  the DOM while folded); `TmDocumentReviewCard` is the one card of a tracked change (revision panel and suggestion panel);
  `TmDocumentVersionPreviewStrip` is the "You are viewing version X" strip of the doc region.
- **Contextual inspector (phase 6, decision DE2).** On a desktop layout a selected image or table, and an open find,
  replace the inspector's tab bar with a header (title, back arrow, X): `TmDocumentSidePanel.ContextContent` /
  `ContextTitle` / `ContextKind` (`data-context="image|table|find"`) / `OnContextBack` / `OnContextClose` - no new
  `DocumentSidePanelTab`. The back arrow and the X dismiss an object's context while that object stays selected; for find
  they close find. Tablet and phone keep the tabs (object inspector in the Properties tab, find as a floating panel).
  A host panel that supplies its own header passes `ShowHeader="false"` to `TmDocumentImageInspector` /
  `TmDocumentFindPanel`.
- **Revision previews.** `TmDocumentRevisionPanel.PreviewResolver` (`Func<DocumentRevision, DocumentRevisionPreview?>`,
  `DocumentRevisionPreview` is a DocumentEditor-package record): the editor reads the text from the document by the
  revision's range; the panel fills what the resolver leaves from the payload (plain text of an insertion / deletion,
  "Bold: on" for a formatting change) - the raw payload is never printed. A long list renders in windows of `PageSize` (default 40, "Show N more"; windowing rather than `Virtualize`, decision `DE-REVISION-PANEL-WINDOWING`: a card has no fixed height). The window always grows to cover `SelectedRevisionId`; the find panel uses the same pattern (100 hits a window, the active hit always rendered, the heading keeps the full count).
- **Document-region modes (phase 7).** Besides editing the doc region has two modes. The print preview is the engine's own state
  (`openPrintPreview` / `closePrintPreview`): while it is up the ribbon is not rendered, the left region holds the page thumbnails
  (`TmDocumentPageNavigator ShowHeader="false"`), the doc region a page / zoom bar and the inspector region (desktop) the action cards and
  the read-only information; a tablet / phone render the actions as a bar inside the doc region. The comparison mode shows
  `TmDocumentDiffViewer` (text aligned by block, `ViewMode` = `DiffViewMode.SideBySide | Unified`, `OldMeta` / `NewMeta`, next / previous
  change, `OnBack`) in `[data-testid="document-diff-mode"]`; the canvas engine stays mounted and the page surface is `hidden`. It is opened by
  `TmDocumentVersionPanel.OnShowDiff(DocumentVersionDiffRequest)` ("Show differences") and `TmDocumentCompareDialog.OnOpenComparison(DocumentCompareOpenRequest)`
  ("Open comparison"); a host that embeds the panel or the dialog without the editor renders the viewer itself. The compare sources are
  `TmDocumentCompareSourceCard`s (`Versions` feeds "Version from history"; `DocumentCompareSource` is not extended). `TmDocumentEditorStatusBar`
  shows "Page X of Y" and, when `OnZoomChanged` / `OnToggleFullscreen` are bound, the zoom and fullscreen controls.
- `TmDocumentDebugModal` + `TmDocumentDebugSection` - the shared frame of the developer debug modals; an empty section
  shows the localized "No data" notice. Panel actions use `tm-btn` (`tm-btn-primary` for the panel's primary action) and
  `tm-input`; the BEM classes stay on the elements as aliases.

## Layout Modes

`TmDocumentEditor` lays itself out from its **own container**, never the viewport: a 390 px host on a 1440 px screen is a phone
layout. `LayoutMode` (`TmLayoutMode`, default `Auto`) measures the editor through the shared `TmLayoutObserver` (first frame
Desktop); `Desktop` / `Tablet` / `Mobile` pin the layout and never import the observer (tests, fixed hosts). The resolved mode
is `data-layout="desktop|tablet|mobile"` on `.tm-document-editor`; the steps are the `TmBreakpoints` constants (640 and 1024 px).

| Container | Regions (`[data-region]`) | Outline and Pages |
|-----------|---------------------------|-------------------|
| Desktop, 1024 px and more | `nav` \| `doc` \| `inspector` under the header and the ribbon | the left column (`TmDocumentNavigationPanel`, foldable - it starts folded into its rail while the container is narrower than navigation + A4 page + inspector + gutters, 1546 px for A4, the reader's choice wins); the inspector has the text tabs Properties, Comments, Revisions, Versions |
| Tablet, 640 - 1023 px | `doc`, with the inspector as ONE side sheet over it (same grid cell) | tabs of the side sheet (`DocumentSidePanelTab.Outline` / `Pages`), read as text tabs |
| Mobile, below 640 px | `doc` only - no inspector region; panels are bottom sheets of the floating root | tabs of the inspector sheet / the action bar "More" |

- **Header.** `ShowHeader` (default `true`) renders `TmDocumentEditorHeader`: the title (`Title`, else the document metadata
  title), `Subtitle`, the save state, Search (opens the find panel), Undo, Redo, Print preview, Print to PDF (only when a
  `PdfExportProvider` exists and the user may export) and the primary Save. `HeaderStart` and `HeaderActions` are the host
  slots (Back, Share - the editor has neither). Undo / Redo / Save render exactly once: in the header, or - with
  `ShowHeader="false"` - in the ribbon quick-access group. The test ids `document-save`, `document-undo`, `document-redo` and
  `document-editor-workspace` are unchanged.
- **`data-layout` is the one source of the structure.** The grid templates / areas of the workspace are
  `.tm-document-editor[data-layout="desktop|tablet|mobile"] .tm-document-editor__workspace` rules, so a forced `LayoutMode`
  (`data-layout-forced="true"`), the markup and the CSS can never disagree. The only container query that touches structure is the
  first-frame guard of an unmeasured Auto editor (`:not([data-layout-forced])`). The header is not a container either: its labels
  and wrapping follow `data-layout` like the ribbon's.
- **Size container.** `@container tm-doc-editor (width ...)` rules answer to
  `.tm-document-editor__main` (the workspace and the status bar) and only tune density (gaps, paddings, the phone panel height) and
  the `cqw` track widths. The root, the ribbon and the floating root are NOT size
  containers on purpose: a container is the containing block of every `position: fixed` descendant, and the ribbon pickers and
  backdrops, the dialogs and the floating root position against the viewport. Anything that needs the layout but must stay
  outside the container (the ribbon, the loading / error states, the root's flat corners) reads `data-layout` instead - keep
  new fixed overlays OUT of `.tm-document-editor__main`.
- **Breakpoints and sheets.** The editor's 640 / 1024 px steps are the `TmBreakpoints` constants and intentionally differ from the 768 /
  1200 px of `TmEditorShell` (an editor-shell decision, not a convention) - do not "align" them. The phase-8 sheets render OUTSIDE
  `.tm-document-editor__main` (siblings of the workspace), for the fixed-descendant reason above.
- **Width reporting is deliberate.** The editor binds `TmLayoutObserver.ContainerWidthChanged`, so the observer reports every
  rounded width change. The handler only acts on crossing the fold width (a report that changes nothing suppresses the implicit
  render); `DocumentEditorCanvasPerfBudgetE2ETests` and the host-resize specs cover it.
- **A page wider than the doc region - the engine fits it (decision `DE-ENGINE-AUTO-FIT`).** The page is fitted to the width of the
  document column by the canvas ENGINE, in every layout: `zoom.autoFit` (`off` | `narrow` | `always`) lives in the engine's view state, a
  `ResizeObserver` on the real scroll viewport (`resolveScrollViewportElement`, never the content-sized mount; re-bound when a host replaces the
  viewport) re-applies it once per animation frame when the width changes (`view/auto-fit-observer.mjs`). **The reader keeps their place:** before the
  scale changes the engine records the page at the top of the view and the fraction of that page above the top edge, afterwards it scrolls the REAL
  vertical scroller (the clipping scroll ancestor, or the window when the host scrolls the page) so the same fraction of the same page is at the top
  again - exact for a mounted page, estimated and re-settled over the next frames for a page the virtualizer dropped; with nothing measurable it falls
  back to a proportional offset. `resolveAutoFitScale` (`view/zoom-controller.mjs`) is the pure rule: in `narrow` a page whose width plus gutters exceeds the column is
  fitted (`fitWidth`), a column that holds it shows 100 percent, the fit never enlarges, the floor is 0.25, a 2 px hysteresis (a fitted page
  lets go only with 2 px plus a scrollbar allowance to spare) and a guard that ignores a width that grew by no more than a scrollbar stop the page
  flipping when a scrollbar appears with the fit. The reader's own zoom (`setZoom`, zoom in / out, ctrl+wheel, pinch, fit page) switches
  `autoFit` off until the `fitWidth` command ("Page width", mode `always`) or `resetZoom` is run - `resetZoom` returns to the mode the host
  configured, which the engine puts into the command context (`metrics.autoFitMode`), and is offered as the status-bar menu entry "Automatic", the
  View tab button "Automatic zoom" and **Ctrl+0**. The snapshot reports `formatting.zoomPreset` and `zoomAutoFit` and `data-canvas-zoom-auto-fit`
  is on the engine root. The engine reports its own re-fits to .NET through `OnCanvasViewChanged`, debounced (trailing 150 ms, only when the
  percent / preset / mode changed - `view/view-change-notifier.mjs`). Blazor owns only `PageFitMode` (`Auto` = `narrow`, default; `Off`;
  `FitWidth` = `always`), forwards it with the mount options / `setOptions` (changing it after mount REPLACES the reader's zoom with the new mode),
  and reflects the engine report: `data-page-fit` on the root while the page is fitted smaller than itself (the ruler gives way then, in every
  layout - a stopgap until the ruler scales with the page) and the status bar label "Fit 62 %" (automatic) / "Page width · 62 %" (the reader's own
  fit), percent formatted for the UI culture. A layout switch asks the engine for one `applyAutoFit` because the padding of the page column belongs
  to the layout. The ruler follows the zoom scale (track width and markers). Sticky ribbon height is published as `--tm-document-ribbon-block-size`
  (observed by `region-cycle.mjs`) so the sticky navigation column parks below it.
- **The readable floor (decision `DE-AUTO-FIT-READABLE-FLOOR`).** Next to a DOCKED inspector the fit of a 1024 - 1136 px container (the demo's
  1046 px editor at a 1440 viewport) would drop the page to about 0.64 (text at two thirds of its size). `TmDocumentEditor` therefore estimates the fit a
  docked layout would give (`EstimatedFitBelowReadableFloor`: container minus the folded navigation rail, the inspector clamp(352, 26 %, 384) and the
  workspace / stage chrome, over the page width) and, when it is below `ReadableAutoFitScale` (0.75; a 1 percent hysteresis on the way back), keeps
  the desktop ribbon and navigation rail and presents ONLY the inspector as the tablet's overlay sheet: `data-inspector-sheet="true"` on the root,
  `data-presentation="sheet"` on the region, CSS in `_document-editor-sheets.css`; the sheet starts closed (an open one would hide half of the page)
  unless the reader chose the panel's state, and the print preview keeps its docked actions column. The estimate is a pure function of the container
  width (and the page), never of the current presentation or of whether the inspector is open, so the sheet cannot flip the answer back; a forced
  `LayoutMode` and `PageFitMode.Off` are never second-guessed. The page then keeps the width the sheet gives back (a 1048 px container holds it at
  100 percent).
- **Why the navigation column still folds below 1546 px.** The phase-3 fold (navigation + A4 page + inspector + gutters) stays: with the fit a
  1200 px container WITHOUT the fold would give the page 576 px (a 0.6 scale, text too small to read), WITH the fold (the 3 rem rail) 800 px
  (about 0.93). The fold buys readable text, the fit guarantees no clipping for the rest; the reader's own choice of the column still wins.
- **The bounded frame (`Height`, phase 12; decision `DE-BOUNDED-FRAME-DOC-SCROLLER`).** `TmDocumentEditor.Height` (`string?`, a CSS length: `600px`,
  `100%`, `calc(100dvh - 5rem)`, `clamp(20rem, 80dvh, 60rem)`, `var(--host-height)`; `null` = today's flow) bounds the editor. Only a number with a unit or a
  `calc` / `min` / `max` / `clamp` / `var` expression with balanced parentheses is accepted - `auto`, `none`, `inherit`, a unit-less number or anything with a
  semicolon / brace / quote is ignored (the editor flows), never written into a style. With a value the root gets `data-frame="bounded"`; the height is the custom
  property `--tm-document-editor-height` on the `TmLayoutObserver` wrapper `.tm-document-editor-host` (also `data-frame="bounded"`), which the root inherits - the
  root renders **no `style` attribute** of its own, so a host's `style` in `AdditionalAttributes` can neither clobber nor be clobbered by the frame. The editor
  becomes a flex column of that height: the header and the ribbon on top (the ribbon is no longer sticky - nothing scrolls under it), then
  `.tm-document-editor__main`, which fills what is left (`flex: 1 1 auto; min-block-size: 0`), and under it the status bar, pinned because nothing scrolls past it
  (a phone's `TmMobileActionBar` is the last in-flow row of the same column). The frame takes its height from the flex chain only (no size containment): a
  percentage `Height` in a parent without a definite height resolves to `auto` and the editor flows at its natural height instead of collapsing.
  The workspace is a one-row grid (`grid-template-rows: minmax(0, 1fr)`, `overflow: hidden`) and **only its regions scroll**, each inside its own cell: the
  **document region** (`[data-region="doc"]`, marked `data-scroll-viewport="true"`) in both axes (`scrollbar-gutter: stable`, `overflow-anchor: none` - the
  virtualizer mounts and unmounts pages under the reader and the browser's scroll anchoring moved the scroll by three pages within 100 ms), the docked navigation
  column and the docked inspector panel (full column height, `overscroll-behavior: contain`, the panel body scrolls). The canvas host inside the document region
  is `overflow: visible` (its own scroller would put the horizontal scrollbar at the end of the document, which is the problem this solves), and the comment rail
  stops being a second scroller. The sticky panels cap with `--tm-document-frame-block-size` (`100dvh` in the flow, `100%` of their cell in a frame; the published
  ribbon size is reset to 0 inside main). Every rule keys on `data-frame` and `data-layout`, never on a viewport media query (`TmDocumentEditorCssTests` walks the
  enclosing at-rules). Sheets, dialogs and the floating root stay OUTSIDE `.tm-document-editor__main` (a container is the containing block of fixed descendants).
  - **`data-scroll-viewport="true"` is a frozen attribute** (editor markup, CSS and the canvas engine share it): the engine treats the nearest ancestor that
    carries it as THE scroll viewport whatever its overflow state says. A host that mounts the engine in its own frame sets it on the element that scrolls the
    document.
  - **Engine.** `entry.mjs`: `resolveScrollViewportElement` (the fit's viewport and its `ResizeObserver` target), `resolveVerticalScroller` (the reading anchor of a
    fit, `scrollerViewTop`), `readViewport()` (the scroll position is how far the engine root is above the TOP of that scroller, the height is its `clientHeight`),
    `scrollToPage` (an unmounted page: root offset + padding + the SCALED page stride, then a settle loop that snaps the real page into place and gives up as soon
    as the reader scrolled) and the scroll listener - a scroll event does not bubble, so ONE capture-phase window listener reacts to the marked scroller that holds
    the mount. Everything that used `scrollIntoView` (find "go to result", the page navigator, a mounted page, a match probe) goes through
    `scrollElementIntoViewWithin`, which scrolls ONLY the marked scroller - the native call scrolls every ancestor, the window and a host's panels included.
    The marked scroller of an element is cached per element and re-validated. The engine has no caret-into-view or drag auto-scroll of its own (the hidden input
    focuses with `preventScroll`), so there was nothing to migrate there. The fit of phase 11 measures the same element, so `fit-page` fits the frame's height.
  - **Virtualization in CSS pixels.** `createPageVirtualizer().plan(pages, viewport, { scale })` brings the pages into CSS pixels (heights times the zoom; the gap
    between pages and the padding are CSS already) before it compares them with the viewport and sizes the spacers: at a zoom below 1 (a fitted tablet / phone)
    the unscaled model mounted pages two or three strides below the reader. The plan also returns `activePageIndex` - the first page that reaches below the top
    quarter of the view (at most 120 px) - and `getPageMetricsJson` uses it, so "Page X of Y" names the page the reader is on, not the overscan page above it.
  - **The reader's place survives a change of the frame.** `captureReadingPosition()` / `restoreReadingPosition()` (engine, `interop.mjs`,
    `TmDocumentCanvasEngineHost.CaptureReadingPositionAsync` / `RestoreReadingPositionAsync`) keep page + fraction across entering / leaving fullscreen and a
    runtime change of `Height`: the editor captures before the state changes and restores after the render.
  - **Fullscreen is the same frame.** `_isFullscreen` renders `data-frame="bounded"` + `data-fullscreen="true"` on the root; CSS
    (`.tm-document-editor[data-fullscreen="true"]`) sets `--tm-document-editor-height: 100dvh` and `position: fixed; inset: 0` (no
    `body.tm-document-editor--fullscreen` selector any more - the body class and the body scroll lock stay, set by `tmDocumentEditor.setFullscreen`). The
    observer wrapper keeps only the HOST's `Height`, so the page does not grow a viewport-tall hole while the root is lifted out of the flow.
  - **Print preview.** The status bar names the mode ("Print preview", the existing key) instead of "Editing body" and hides its zoom (the preview has its own).
  - **Phone.** The bar sits under the workspace (and under the status bar when it has something to say). The core `TmMobileActionBar` pads its bottom by
    `env(safe-area-inset-bottom)` itself, so the editor adds nothing on top of it; without a bar the status bar takes that padding (`:last-child`).
  - **Demo.** `/canvas-engine-host?height=viewport` gives the editor exactly the viewport under the demo chrome (`.demo-document-frame`); any other `height` value is
    passed through as a CSS length. `/document-editor` is bounded by default: a flex column `.demo-document-page` of the same height
    (`--demo-shell-chrome-block-size`, one variable for both pages) with the capped controls on top and the editor (`Height="100%"`) taking the rest, so the page does
    not scroll; the checkbox `document-editor-bounded` (or `?bounded=false`) returns to the flowing editor with the building-block cards below it. The E2E specs
    of the flowing editor open the page with an explicit `bounded=false` (`FlowingDocumentEditorUrl`), the specs of the frame with `bounded=true`.
- **Doc region banner slot.** `.tm-document-editor__doc-banners` (`data-testid="document-doc-banners"`) sits above the page
  inside `[data-region="doc"]`; the review summary and the version preview strip (`TmDocumentVersionPreviewStrip`) live there, the diff banner joins later.
- **F6.** F6 / Shift+F6 cycle the landmarks marked `data-focus-region`: header, ribbon, navigation, document, inspector (a
  region that is absent or hidden is skipped; a modal surface owns the key while it is up). The handler is the small module
  `js/document-editor/interop/region-cycle.mjs` because the browser default of F6 must be cancelled synchronously. The ribbon
  keyboard mode (Alt / F10, `data-ribbon-keyboard-mode`) is separate Blazor state and is not touched.

## Header and host slots (G9)

`ShowHeader` (default `true`) renders `TmDocumentEditorHeader`: title (`Title`, else the document metadata title), `Subtitle`, the save
state, Search, Undo, Redo, Print preview, Print to PDF (only with a `PdfExportProvider` and the export right) and the primary Save. The editor
owns neither "Back" nor "Share" (decision G9 - the application shell is the host's): `HeaderStart` (before the title) and `HeaderActions` (after
the editor's own actions; behind the kebab on a phone) are the slots. Undo / Redo / Save render exactly once - in the header, or, with
`ShowHeader="false"`, in the ribbon quick-access group. `TmDocumentEditorHeader.Compact` is the phone row (title, state icon, Search, Undo,
Redo, kebab); the kebab is a `role="dialog"` core overlay panel (a bottom sheet on a phone) whose rows are full-width, one touch height.
Test ids `document-save`, `document-undo`, `document-redo`, `document-header-*` are stable.

## Contextual inspector and find (DE2)

The inspector is ONE fragment (`TmDocumentSidePanel`) hosted by the docked region (desktop), the side sheet (tablet) or a bottom sheet (phone).
On a desktop a selected image or table, and an open find, replace the tab bar with a contextual header (`ContextContent` / `ContextTitle` /
`ContextKind` / `OnContextBack` / `OnContextClose`; no new `DocumentSidePanelTab` value); a tablet puts the same context in the side sheet, a phone
opens find as its own full-height sheet and an object as a short sheet on request ("Properties"). Text tabs are used on a desktop AND a tablet
sheet (24 rem is wide enough; the row scrolls with an edge fade when a long label needs it) and icon tabs - the label as the accessible name - on
a phone. Revision cards take their text from `TmDocumentRevisionPanel.PreviewResolver` (`Func<DocumentRevision, DocumentRevisionPreview?>`, the
editor reads the range from the document; a resolver that returns `null` falls back to the payload's human line, never the raw payload).

**When the inspector opens (decision `DE-INSPECTOR-OPENS-ON-NEW-OR-DELIBERATE-SELECTION`).** The inspector opens for a NEW object
(a different object id than the one the editor last saw selected) or for a DELIBERATE selection of the one already selected - never because the
engine re-reported it. The canvas engine stamps every object selection notification with `objectSelectionSource` (carried to the host as
`WysiwygMiniToolbarRequest.ObjectSelectionSource`): `pointer` = the release of a click on the object that did not drag, `keyboard` = Tab-cycling
between objects, `layout` = everything else (a re-layout, a drag / resize / rotate preview, the host's state re-render). The stamp is one-shot - the
next notification is `layout` again. Only `pointer` and `keyboard` forget a dismissal, reopen a closed panel (non-phone layouts) and move an open
panel standing on another tab (comments, revisions ...) to Properties; a `layout` report, a caret move and an empty report change nothing, so a
reader who closed the inspector while the image stays selected does not see it pop back after the next edit. Loading, importing or restoring a
document forgets the selected image (`ResetInspectorSelectionMemory`), so an object id that repeats in the next document opens the inspector again.

## Document-region modes and the comparison

Besides editing the doc region has two modes (phase 7). The **print preview** is the engine's own state (`openPrintPreview`): no ribbon, page
thumbnails on the left, the page / zoom bar above the page, the action cards and the read-only information on the right (desktop) or in a bar
inside the doc region (tablet / phone). The **comparison** shows `TmDocumentDiffViewer` (`ViewMode` = `DiffViewMode.SideBySide | Unified`,
`OldMeta` / `NewMeta`, `Compact` for the phone's Older / Newer / Differences switch) inside `[data-testid="document-diff-mode"]`; the canvas engine
stays mounted (one canvas, never a copy) and the page surface is `hidden`. It owns the whole workspace on a desktop and a tablet - the docked
inspector steps aside - so a panel asked for from the palette, the ribbon or Ctrl+Alt+V (comments / revisions / versions) first leaves the
comparison ("Back to document") and then shows the panel; a phone's panels are sheets and stay over the comparison. A command that changes the
document leaves the comparison before it runs; a view command (zoom, fullscreen, print preview) does not. The diff is text by block (decision
DE3), never formatted; Replace-all / accept-all and friends follow the usual registry rules.

## Phone scope (decision G2)

A tablet edits in full; a phone views and makes light edits (see [document-editor-tablet-mobile.md](document-editor-tablet-mobile.md) for the full
table). `DocumentEditorCommandRegistry.PhoneRestrictedCommands` is the registry half of that scope - the REGISTERED commands `insertEquation`,
`cellProperties` and `compareDocuments` are disabled whatever the context says while `DocumentEditorCommandContext.IsPhoneLayout` is set, so the
palette and the keyboard cannot reach what the phone chrome hides. The set only bites on registered commands: page layout and the style
management are toolbar buttons and dialogs the phone ribbon simply does not render. The set is a static read-only set owned by the editor; a registered command can read `DocumentEditorCommandContext.IsPhoneLayout` in its own `computeEnabled`.

## The `data-priority` contract

Every ribbon command keeps `data-command`, `data-testid` and publishes `data-priority` (`primary` / `secondary` / `overflowonly`) - the rank
declared in `DocumentEditorBuiltInToolbar` (`DefaultItems`, `RibbonLocalCommands`). It is a stable contract for hosts, tests and the Modeling
package: key off those attributes (and `data-tm-group`), never off the toolbar's internal DOM or a positional selector. Which commands fit is
measured by the core `tm-toolbar.js` (lowest rank leaves first, the later of equal ranks first); a phone re-ranks the row for its width (`PhoneKeptCommands`) without changing the published value. Ribbon icons are unique per tab (`ViewTab_NeverGivesTwoCommandsTheSameIcon`).

## Responsive limits that stay (known and documented)

- The command palette is keyed on `data-layout` and is fullscreen for a pinned phone layout.
- With `LayoutMode.Auto` the editor's own container decides the layout, but the modal dialogs and the sheets of the floating root resolve the viewport
  (see "Auto layout caveat" below); pin `LayoutMode` to make them follow the editor.

## Dialogs, sheets and the keyboard (phase 13)

- **One source for "sheet or popover".** `TmDocumentFloatingPanel` renders from `TmOverlayPanel.IsSheetChanged` only: a forced
  `LayoutMode.Mobile`, a viewport scope and the probe all resolve in the core, which reports the FIRST resolution too. The wrapper has no
  cascading viewport and no `ResolvedLayoutChanged` handler; it keeps the explicit `Anchor`.
- **Dialogs answer to their own width and to the layout.** The compare dialog's source grid is an intrinsic
  `repeat(auto-fit, minmax(min(100%, 18rem), 1fr))`; the style gallery form, the image library and the page-layout form are narrow by default and
  widen in one `@container tm-doc-floating (width >= 640px)` block (a `TmBreakpoints` constant, the container is the panel body, never the element
  the query styles), gated `:not([data-layout="mobile"])`. The resolved layout wins over the width: `.tm-modal-overlay[data-layout="mobile"]` (the
  modal's overlay reads the viewport scope that a forced `LayoutMode` fills) gives the compare dialog its single column, a readable 40rem column
  and finger-sized footer buttons even on a 1440 px viewport. There is no `@media` on a width in the package (guarded by `TmDocumentEditorPhase13CssTests`).
- **Auto layout caveat.** With `LayoutMode.Auto` the editor measures its OWN container, but the core `TmModal` dialogs resolve the VIEWPORT (no
  scope is cascaded): an editor in a narrow column on a wide screen gets desktop-presented dialogs. A host that wants the dialogs to follow the
  editor pins `LayoutMode` (a forced mode cascades the viewport scope to the dialogs and the floating panels).
- **Phone dialogs above the keyboard.** The version dialog is a sheet (lifted by the core sheet tracker; the focus opens on its label field); the
  compare dialog is fullscreen on a phone and passes `TrackVirtualKeyboard="true"` (the focus opens on the "Change..." button of the first missing
  source). A bounded phone frame attaches the core `tm-visual-viewport.js` to its root through `VisualViewportInterop` and gives
  `--tm-visual-viewport-inset-block-end` up as padding, so the pinned status bar and the action bar are above the keyboard (the inset is measured
  relative to the frame).
- **The floating toolbar follows the frame.** The engine clips the mini / object toolbar payload against the doc-region scroller (`miniToolbarClipRect`):
  outside it the payload is `isVisible: false` with the reason `canvas-selection-scrolled-out` (an object keeps its selection source, so the
  inspector still opens for an object the reader chose), partly visible it is clamped into the clip rect - below the selection when there is no
  room above it - and a scroll that flips the visibility re-reports it as a `layout` report.

## What the redesign did not do

Share / "back" / the application shell, transactional footers (G8: every change applies at once and is undoable), a formatted diff, a Blocks tab
and the Word features of the background of the references (DE-1.9 / DE-2.8 stay in the backlog).

## Accessibility Expectations

Toolbar, command palette, grid picker, table/image panels, paste report a live region musí být ovladatelné klávesnicí. Dialogové plochy mají mít role/aria modal tam, kde drží focus. Statusy save/autosave/find/paste mají být oznamované přes live region bez rušivého focus skoku.

Compact toolbar nesmí přijít o aria labely jen proto, že skrývá text. Overflow menu musí mít menu role, aktivní položku a šipkovou navigaci.

## Demo Route Checklist

Route `/document-editor` má pro knihovní uživatele pokrývat:

- toolbar mode switch,
- feature toggles pro images, tables a review,
- image provider enabled/disabled stav,
- table properties sample document,
- comments and review sample data,
- paste report sample HTML,
- autosave error provider toggle.

## Headless export parity: host fonts (decision `DE-EXPORT-PARITY-HOST-FONTS`)

The headless export (a snapshot-less PDF / DOCX of a stored document) lays the document out from the advance tables of the faces the HOST registers,
while the live canvas measures with whatever the browser resolves the theme's font stack (`Aptos, Arial, sans-serif`) to. The two paginate the same only
when the host's font catalog resolves the SAME faces the browser does for that stack: on Linux `Arial` resolves to Liberation Sans (the metric-compatible
substitute), not DejaVu Sans, which is ~8 % wider with taller lines (the contract demo was 3 server pages against 4 browser pages). A host catalog
therefore registers ONE family as a whole - never a bold of one family next to the regular of another - and prefers the metric-compatible family; the demo's
`DemoDocumentExportFontCatalog` probes Windows Arial, then Liberation Sans in the Debian (`/usr/share/fonts/truetype/liberation/`), Fedora
(`/usr/share/fonts/liberation-sans/`, `/usr/share/fonts/liberation/`) and Arch (`/usr/share/fonts/TTF/`) locations, then DejaVu Sans, and exposes the
chosen `FamilyName`. A host without any known face keeps failing closed (`TempoDocumentLayoutException`) rather than degrading to non-WYSIWYG output.
