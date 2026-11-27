# TmDocumentEditor Developer Guide

Tento dokument shrnuje integrační body, které vznikly během CKEditor-inspired redesignu `TmDocumentEditor`. Praktické demo je na route `/document-editor`; ukazuje toolbar režimy, feature toggles, image provider, table properties, comments/review, paste report a autosave error scénář.

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
- **Phone:** the phone layout keeps a horizontal scroll fallback until its own command row (a later phase).

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
| Tablet, 640 - 1023 px | `doc` \| `inspector` | tabs of the inspector (`DocumentSidePanelTab.Outline` / `Pages`) |
| Mobile, below 640 px | one column, the inspector under the document | tabs of the inspector (sheets and the action bar arrive with the phone phase) |

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
- **A page wider than the doc region.** Nothing clips the surface: the canvas host is the horizontal scroller, so a clipped A4
  page is reachable by scrolling. An automatic fit-width zoom is planned (phase 8), and the sticky ribbon height is published as
  `--tm-document-ribbon-block-size` (observed by `region-cycle.mjs`) so the sticky navigation column parks below it.
- **Doc region banner slot.** `.tm-document-editor__doc-banners` (`data-testid="document-doc-banners"`) sits above the page
  inside `[data-region="doc"]`; the review summary and the version preview strip (`TmDocumentVersionPreviewStrip`) live there, the diff banner joins later.
- **F6.** F6 / Shift+F6 cycle the landmarks marked `data-focus-region`: header, ribbon, navigation, document, inspector (a
  region that is absent or hidden is skipped; a modal surface owns the key while it is up). The handler is the small module
  `js/document-editor/interop/region-cycle.mjs` because the browser default of F6 must be cancelled synchronously. The ribbon
  keyboard mode (Alt / F10, `data-ribbon-keyboard-mode`) is separate Blazor state and is not touched.

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
