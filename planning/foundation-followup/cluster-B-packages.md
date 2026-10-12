# Cluster B: non-promoted z-index dialogs (overlays.md rule 9) and DataTable export rows

Read-only analysis. Only this file was written: no source changes, no commits, no plan edits.

## Baseline facts

- **Token values** from `src/Tempo.Blazor/wwwroot/css/tokens.css` L228–247: `--tm-z-dropdown` 1000, `--tm-z-sticky` 1020, `--tm-z-popover` 1030, `--tm-z-drawer-backdrop` 1035, `--tm-z-drawer` 1036, `--tm-z-modal` **1040**, `--tm-z-overlay` 1060, `--tm-z-toast` 1080, `--tm-z-tooltip` 1090.
- **Notion tokens** from `_notion-variables.css` L130–136: `--tm-notion-z-context-menu` 800. The `--tm-notion-z-menu` token is **not defined anywhere**, so its fallback of `200` applies.
- **`TopLayerInterop` is not available to packages.** `src/Tempo.Blazor/Helpers/TopLayerInterop.cs` L22 declares it `internal sealed class TopLayerInterop`. `src/Tempo.Blazor/Tempo.Blazor.csproj` L87–91 has a single `InternalsVisibleTo`, for `Tempo.Blazor.Tests`. No package can call promote, raise or pin.
- **The public routes into the top layer** for a package are: `TmModal` (`popover="manual"` at L23), `TmDialog` (L24), modal `TmDrawer`, and `TmOverlayPanel` (L107) for popovers and menus. That also covers `TmDropdown`, `TmPopover` and `TmContextMenu`, which use `TmOverlayPanel`. `TmFocusScope` is public but does **not** promote.
- **Rule 9 text is at `docs/overlays.md` L193–200.**

---

## TASK A + B: inventory and plan coverage, per package

Abbreviations: **NP** = non-promoted (no `popover`, paints in its z-index band). "Trap" means the focus-trap mechanism.

### 1. Core: rich-text-editor (RTE) dialogs (`src/Tempo.Blazor/Components/Activity/`)

All five are hosted by `TmRichEditorFull.razor` L57–103. `TmRichEditorSimple.razor` L46–48 also hosts `LinkDialog`, and `TmMarkdownEditor.razor` L41–52 hosts `LinkDialog` and `TableDialog`. Each dialog is a plain `<div class="tm-rte-*-dialog tm-rte-dialog">` with a sibling backdrop div and a content div. None has `role="dialog"`, `aria-modal`, an Escape handler or a focus trap; a grep for `role=|aria-modal|Escape|FocusAsync|keydown` across `*Dialog.razor` in `Activity/` returns 0 hits.

| File | Component | Render mechanism | z-index (file:line, value) | Trap | popover? | Effort | Covered by plan? |
|---|---|---|---|---|---|---|---|
| `Activity/LinkDialog.razor` L3–48 | LinkDialog | Own div overlay: `.tm-rte-link-dialog` (L5), backdrop (L6), content (L7) | `_link-dialog.css` L6 fixed, L11 `var(--tm-z-modal)` = **1040**; content L34 `z-index:1` | none | no | S | **NOT COVERED**: no RTE plan in app 3bc0cab8 (list of 38 plans) |
| `Activity/ImageDialog.razor` L5–7 | ImageDialog | same pattern | `_image-dialog.css` L6/L8 `var(--tm-z-modal, 1050)` = **1040** (fallback unused) | none | no | S | NOT COVERED |
| `Activity/TableDialog.razor` L5–7 | TableDialog | same (plus a grid picker) | `_table-dialog.css` L6/L8 = **1040** | none | no | S | NOT COVERED |
| `Activity/VideoDialog.razor` L5–7 | VideoDialog | same | `_video-dialog.css` L6/L8 = **1040** | none | no | S | NOT COVERED |
| `Activity/FindReplaceDialog.razor` L5–7 | FindReplaceDialog | same (modal with backdrop) | `_findreplace-dialog.css` L6/L8 = **1040** | none | no | S–M (Replace/Replace All state, result count) | NOT COVERED |

Shared CSS lives in `_rte-dialog-shared.css` L23–31 (form-group rules scoped under `.tm-rte-dialog`).

**Aggregate effort: M.** The work is five small forms moved onto `TmModal` (Size Small, `Footer`) across three hosts. The editor selection and caret must be restored to the contenteditable on close (`RestoreFocusTarget`). The `tm-rte-*` content and button classes stay for existing tests.

### 2. Core: dashboard widget selector

| File | Component | Render mechanism | z-index | Trap | popover? | Effort | Covered by plan? |
|---|---|---|---|---|---|---|---|
| `Components/Dashboard/TmWidgetSelector.razor` L7–8, hosted at `TmDashboard.razor` L298–306 | TmWidgetSelector | Own div overlay `.tm-widget-selector-overlay` (click closes) containing `.tm-widget-selector-modal`. No `role`, `aria-*`, Escape or trap (grep of the file: 0). | `_dashboard.css` L415–425: fixed L416, `var(--tm-z-modal)` L420 = **1040** | none | no | M | **COVERED.** Dashboard plan `d254e3dd`, phase "Fáze 1: Opravy, lokalizace a a11y (vlna 0, bez závislosti na základu)", subtask B12: *"`TmWidgetSelector.razor` přestavět na `TmModal` (Size Large, vlastní hlavička/patička) — `role="dialog"`, `aria-modal`, `aria-labelledby`, focus trap, Escape, scroll lock; ponechat třídu `tm-widget-selector-modal` … Odstranit vlastní overlay CSS (`_dashboard.css` ř. 378–388 — z-index 1000, `rgba`)"*. "Fáze 6: Redesign TmWidgetSelector" restates it: *"widget selector (už na `TmModal` z Fáze 1)"* and *"Mobil: `MobilePresentation` sheet/fullscreen"*. |

⚠ The plan's line references are stale. The overlay rule is now at `_dashboard.css` **L415–425**, and its z-index is `var(--tm-z-modal)`, not `1000`/`rgba`.

The other z-index entries in `_dashboard.css` (L94, L172, L202, L211 `.tm-widget--dragging` 1000, L315, L373) are in-grid layering, not dialogs. The other dashboard dialogs are already promoted: the new-dashboard dialog is a `TmModal` (`TmDashboard.razor` L310) and the delete confirmation is a `TmDialog` (L336).

### 3. Tempo.Blazor.NotionEditor

**Already promoted (no longer exceptions):**
- `TmDialog` delete confirmations in `UI/TmNotionTextCommentPanel.razor` L300, `UI/TmNotionBlockCommentPanel.razor` L471 and `UI/TmNotionPageCommentPanel.razor` L344.
- `TmOverlayPanel`-based menus: `TmNotionAiMenu` (comment L9–10), `TmNotionMentionMenu` (L10–11), `TmCommentMentionInput` (L16–17) and `TmNotionNotificationCenter` (L28).

**Non-promoted modal surfaces.** All paths are relative to `src/Tempo.Blazor.NotionEditor/Components/NotionEditor/`. "Notion plan" means `c8d4bcd2-40ae-4c22-8c3e-08df2362aa32`. "F4" means its phase "Fáze 4: Sjednocení dialogů na TmModal/sheet — stránka, sidebar, databáze a komentáře bloku/textu", whose pattern line reads *"desktop centrovaný `TmModal` … mobil sheet … žádný vlastní backdrop ani z-index"*. F6 and F7 are referenced the same way.

| File (razor) | Component | Render mechanism | z-index (scoped CSS file:line, value) | Trap | popover? | Effort | Covered by plan? |
|---|---|---|---|---|---|---|---|
| `Page/TmNotionShareDialog.razor` L7–13 | TmNotionShareDialog | Backdrop div plus `<section role=dialog aria-modal=true>` | `TmNotionShareDialog.razor.css` L2/L4 `calc(--tm-z-modal−1)` = 1039; L9/L10 = **1040** | none (no Esc/focus code) | no | S–M | **COVERED** F7 (N8): *"`Page/TmNotionShareDialog` → `TmModal`/sheet"* |
| `Page/TmNotionRestrictionsDialog.razor` L7–12 | TmNotionRestrictionsDialog | same | `…RestrictionsDialog.razor.css` L2/L4 1039; L9/L10 **1040** | none | no | S–M | **COVERED** F7 (N8): *"`Page/TmNotionRestrictionsDialog` → `TmModal`"* |
| `Page/TmNotionPageInfoPanel.razor` L6–11 | TmNotionPageInfoPanel | Backdrop plus `<aside role=dialog aria-modal>` | `…PageInfoPanel.razor.css` L2/L4 `--tm-z-overlay` = 1060; L14/L17 **1061** | none | no | S | **COVERED** F4 (N1): *"`TmNotionPageInfoPanel` → modal na desktopu a sheet na mobilu"* plus *"Odstranit vlastní backdropy a z-indexy (… `TmNotionPageInfoPanel.razor.css` ř. 1–27 …)"* |
| `Page/TmNotionPageHeader.razor` L40–44 | Cover dialog (inline in header) | Backdrop plus `div.tm-notion-cover-dialog role=dialog aria-modal` | `TmNotionPageHeader.razor.css` L84/L86 `var(--tm-notion-z-menu, 200)` = **200** (undefined token); L89/L93 **201** | Esc only (2 Escape hits in the file) | no | S | **COVERED** F4 (N1): *"`Page/TmNotionPageHeader.razor` ř. 40–93 (obálka) → `TmModal` Small/Medium"* |
| `Page/TmNotionPageHeader.razor` L218–223 | Inline emoji picker | Backdrop plus `div role=dialog aria-modal` | same CSS L455/L457 **200**; L460/L463 **201** | Esc only | no | S | **COVERED** F4: *"Inline emoji picker ř. 218–280 obalit `TmModal`"*. F6 then deletes the inline copy. |
| `Blocks/TempoBlocks/TmNotionDiagramEditModal.razor` L6–9 | TmNotionDiagramEditModal | Root div `role=dialog aria-modal` hosting a DynamicComponent editor | `…DiagramEditModal.razor.css` L2 fixed, L4 `var(--tm-z-modal,1040)` = **1040** | none (0 Escape/focus hits) | no | M (shared wrapper) | **COVERED (implicitly)** F7 (N12): *"Interní `TmNotionTempoEditorModal` … použijí `Blocks/TempoBlocks/TmNotion{Diagram,Spreadsheet,Wireframe}EditModal`"*. The goal line is *"zbylé dialogy … na vzoru Fáze 4"* (the TmModal pattern). The wrapper is not literally said to be built on `TmModal`; suggest making that explicit. |
| `…/TmNotionSpreadsheetEditModal.razor` L4–7 | TmNotionSpreadsheetEditModal | same | `…SpreadsheetEditModal.razor.css` L2/L4 **1040** | none | no | M | same as above (F7 N12) |
| `…/TmNotionWireframeEditModal.razor` L5–8 | TmNotionWireframeEditModal | same | `…WireframeEditModal.razor.css` L2/L4 **1040** | none | no | M | same as above (F7 N12) |
| `Blocks/Database/TmNotionDbRecordDetail.razor` L7–9 | TmNotionDbRecordDetail | `div.tm-dbrd__overlay role=dialog aria-modal` (click closes) wrapping `__modal` | `…DbRecordDetail.razor.css` L7/L9 **1040** | none | no | M | **COVERED** F4 (DB-3): *"`TmNotionDbRecordDetail` + `TmNotionDbTemplateEditor` → sdílený layout záznamu … `TmModal` Large"* |
| `Blocks/Database/TmNotionDbTemplateEditor.razor` L7–9 | TmNotionDbTemplateEditor | same | `…DbTemplateEditor.razor.css` L7/L9 **1040** | none | no | M | **COVERED** F4 (same quote) |
| `Blocks/Database/TmNotionDbImportExport.razor` L6–9 | TmNotionDbImportExport | Overlay plus `div role=dialog aria-modal` | `…DbImportExport.razor.css` L8/L10 **1040** | none | no | M | **COVERED** F4: *"`TmNotionDbImportExport` → `TmModal` s fixní patičkou"* |
| `UI/TmNotionMediaUploadDialog.razor` L7–14 | TmNotionMediaUploadDialog | Backdrop wrapping `div role=dialog aria-modal` | `…MediaUploadDialog.razor.css` L6 fixed, L8 **1040** | **`tmNotionEditor.initFocusTrap`** (`.razor.cs` L117 init, L280 destroy; `notion-editor.js` L532) | no | M | **COVERED** F7 (N11): *"`UI/TmNotionMediaUploadDialog` → `TmModal` Medium/Large"* |
| `UI/TmNotionPageHistory.razor` L11–15 | TmNotionPageHistory | Backdrop plus `div.tm-nph role=dialog aria-modal` | `…PageHistory.razor.css` L4/L6 1059; L20/L22 **1060** | none | no | L (CSS file is 891 lines) | **COVERED** F7 (N9): *"`UI/TmNotionPageHistory` → `TmModal` ExtraLarge (pod 1024 Fullscreen)"* |
| `UI/TmNotionPageHistory.razor` L300–305 | Restore confirmation (nested) | Backdrop plus `div role=alertdialog aria-modal` | same CSS L665/L667 **1070**; L672/L673 **1071** | none | no | S | **COVERED** F7 (N9): *"potvrzení obnovení přes `TmDialog`"* |
| `UI/TmNotionPageSearch.razor` L10–24 | TmNotionPageSearch | Backdrop, `div.tm-nps role=presentation`, card `role=dialog aria-modal` | `…PageSearch.razor.css` L4/L6 1059; L20/L22 **1060** | Esc plus keydown fence; no trap | no | M | **COVERED** F6 (N10): *"`UI/TmNotionPageSearch` → `TmModal` (mobil fullscreen)"* |
| `UI/TmNotionTemplateGallery.razor` L7–12 | TmNotionTemplateGallery | Backdrop wrapping `section role=dialog aria-modal` | `…TemplateGallery.razor.css` L2/L4 `var(--tm-z-modal)` = **1040** | none | no | S–M | **COVERED** F6 (N10): *"`UI/TmNotionTemplateGallery` → `TmModal`"* |
| `UI/TmNotionShortcutsPanel.razor` L5–18 | TmNotionShortcutsPanel | `div.tm-nsp` (fixed root) with a scrim button and `section role=dialog aria-modal` | `…ShortcutsPanel.razor.css` L2/L4 `calc(--tm-notion-z-context-menu + 80)` = **880** | Esc only | no | S | **COVERED** F6 (N10): *"`UI/TmNotionShortcutsPanel` → `TmModal`/sheet"* |
| `UI/TmNotionBlockCommentPanel.razor` L11–20 | TmNotionBlockCommentPanel | Backdrop plus a positioned card `role=dialog aria-modal` (popover-like) | `…BlockCommentPanel.razor.css` L4/L6 1029; L13/L14 **1030** | Esc only | no | L | **PARTIAL.** F4 (P3) adds `CommentPresentation=Dialog/Auto`: *"Dialog = `TmModal` Large"*. But *"`Auto` + Desktop = dnešní popover (regrese `.tm-nbcp`)"* and *"Popover: clamp přes jádrový `positionFloating`"*, so the **desktop popover stays NP** (it is not moved onto `TmOverlayPanel`). |
| `UI/TmNotionTextCommentPanel.razor` L10–19 | TmNotionTextCommentPanel | same | `…TextCommentPanel.razor.css` L4/L6 1029; L13/L14 **1030** | Esc only | no | L | **PARTIAL** (same as above) |

**Popover/menu-class Notion surfaces** (`role=dialog` without `aria-modal`, or menus with their own fixed backdrop). These are not modal dialogs; they are listed only for completeness.

- **Picker popovers** (F6): `UI/TmNotionEmojiPicker` (CSS L4/L6 1029, L12/L13 1030); `Shared/TmNotionEmojiPicker` (L10/L12 200; F6 deletes it); `UI/TmNotionColorPicker` (L4/L6, L12/L13; F6 deletes it).
- **Menus that F6 moves onto the core popover:** `TmNotionSlashMenu` (1029/1030), `TmNotionTokenDropdown` (1029/1030) and `TmNotionStatusPicker` (1000/1001). F6 (N6) says they are converted to `TmNotionFloatingMenu`, which F3 defines as *"tenký obal nad jádrovým popoverem/dropdownem z F3"*.
- **Inline database panels** (F4 DB-3): `TmNotionDbFieldEditor` `.tm-dbfe` and `TmNotionDbTemplateMenu` `.tm-dbtm`, which are inline rather than fixed, plus the DB float/ctx menus (`TmNotionDatabaseBlock.razor.css` L292–299 and L430–437, 999/1000).
- **Inline sidebar panel:** `Sidebar/TmNotionPageTree.razor` L51–56 and L103–107 bulk panels (inline). F4 (N3) moves them to `TmModal`/`TmDialog`.
- **Not explicitly named in any phase:** `Blocks/TmNotionBlockContextMenu` (`.razor.css` L8/L10 999, L22/L26 1000) also uses `tmNotionEditor.initFocusTrap` (`.razor.cs` L71/L283). The phase infrastructure (F3 N5) says *"Všechny overlaye balíčku stojí na jednom popover/sheet mechanismu jádra"*.
- **Full-viewport mode, not a dialog:** `TmNotionEditor.razor.css` L356/L358, `.tm-notion-editor--presentation` fixed at `var(--tm-z-modal)`.

⚠ **Stale plan text.** F4 says *"Odstranit 3× vlastní overlay (`z-index:1200`)"*, but the DB overlays are now `var(--tm-z-modal, 1040)`.

⚠ **Real-world risk.** The demo `Demo.SharedUI/Pages/NotionEditorPage.razor` L838–881 hosts the Notion editor inside a `TmModal` ("CF18-7"). Every NP Notion dialog above opened from there paints **under** that promoted modal.

### 4. Tempo.Blazor.Spreadsheet

Paths are relative to `src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/`. No `TmModal`, `TmDialog`, `TmFocusScope` or other focus trap exists anywhere in the package (grep of `*.razor`: 0). The dialogs use their own `@onkeydown` for Esc/Ctrl+Enter. "SS plan" means `8959eebe-d7ec-4e83-8c3a-08df2362aa32`, and "SS F3" its phase "Fáze 3: Všech 14 dialogů na jádrový TmModal (+ mobilní prezentace)". The SS F3 subtask reads *"Migrace všech dialogů na `TmModal` (… `MobilePresentation=Auto` …); odstranit vlastní backdropy a `.tm-spreadsheet-dialog-backdrop`, `.tm-fcd`/`.tm-dvd` overlay CSS"*, and its test is *"`Dialog_UsesTmModal_WithAriaModal_ForAll14Dialogs` (theory přes Sort, CustomFilter, RemoveDuplicates, SpecialPaste, TextToColumns, Hyperlink, NameManager, NamedRangeEdit, DataValidation, FormatCells, InsertImage + alerty)"*.

| File | Component | Render mechanism | z-index | Trap | popover? | Effort | Covered by plan? |
|---|---|---|---|---|---|---|---|
| `Dialogs/TmSpreadsheetSortDialog.razor` L5–10 | Sort | Backdrop div wrapping `div role=dialog` (no aria-modal) | `…SortDialog.razor.css` L2 fixed, L4 **1100 (hard-coded)** | own keydown | no | S | **COVERED** SS F3 (named in theory) |
| `Dialogs/TmSpreadsheetCustomFilterDialog.razor` L4–6 | CustomFilter | same | `…CustomFilterDialog.razor.css` L2/L4 **1100** | own keydown | no | S | **COVERED** SS F3 |
| `Dialogs/TmSpreadsheetRemoveDuplicatesDialog.razor` L4–6 | RemoveDuplicates | same | `…RemoveDuplicatesDialog.razor.css` L2/L4 **1100** | own keydown | no | S | **COVERED** SS F3 |
| `Dialogs/TmSpreadsheetSpecialPasteDialog.razor` L4–6 | SpecialPaste | same | `…SpecialPasteDialog.razor.css` L2/L4 **1100** | own keydown | no | S | **COVERED** SS F3 |
| `Dialogs/TmSpreadsheetTextToColumnsDialog.razor` L4–6 | TextToColumns | same | `…TextToColumnsDialog.razor.css` L2/L4 **1100** | own keydown | no | M (3 steps) | **COVERED** SS F3 |
| `Dialogs/TmSpreadsheetHyperlinkDialog.razor` L4–9 | Hyperlink | Backdrop plus `role=dialog`. **No CSS exists** (grep of `hyperlink__*` in CSS: 0), so it renders unstyled in flow. | none | own keydown | no | S | **COVERED.** SS F1 adds temporary CSS at `var(--tm-z-modal)`: *"Dočasné řešení — ve Fázi 3 přejde na `TmModal`"*. SS F3 does the migration. |
| `Dialogs/TmSpreadsheetNameManagerDialog.razor` L4–6 | NameManager | same (no CSS) | none | own keydown | no | M (stacking with NamedRangeEdit) | **COVERED** SS F1 (temporary) and SS F3 (`Dialog_Stacked_NamedRangeEditOverNameManager_EscClosesTopOnly`) |
| `Dialogs/TmSpreadsheetNamedRangeEditDialog.razor` L4–6 | NamedRangeEdit | same (no CSS) | none | own keydown | no | S | **COVERED** SS F1 and SS F3 |
| `TmSpreadsheetFormatCellsDialog.razor` L5–6 | FormatCells | `.tm-spreadsheet-dialog-backdrop` wrapping `.tm-fcd` (no role) | `_spreadsheet.css` L546 fixed, L552 `var(--tm-z-modal)` = **1040**; `.tm-fcd` L640 | own keydown | no | M | **COVERED** SS F3 |
| `TmSpreadsheetDataValidationDialog.razor` L4–5 | DataValidation | same backdrop wrapping `.tm-dvd` (no role) | `_spreadsheet.css` L552 **1040**; `.tm-dvd` L962 | own keydown | no | M | **COVERED** SS F3 |
| `TmSpreadsheet.razor` L203–218 | Insert Image (inline markup) | Same backdrop wrapping `.tm-spreadsheet-dialog` (no role) | `_spreadsheet.css` L545–556, **1040** | none | no | S | **COVERED** SS F3: *"Insert Image vyčlenit z `TmSpreadsheet.razor` ř. 203–218 do `Dialogs/TmSpreadsheetInsertImageDialog.razor`"* |
| `TmSpreadsheet.razor` L300–311 and L313–325 | Validation error and confirm alerts | Same backdrop wrapping `.tm-spreadsheet-alert` | **1040** | none | no | S | **COVERED** SS F3: *"validační alerty do `Dialogs/TmSpreadsheetValidationAlert.razor`"* (on `TmModal`, per the theory test "+ alerty") |
| `TmSpreadsheetCanvasGrid.razor` L80–104 and L106–130 | Column-width and row-height resize dialogs | `.tm-spreadsheet-resize-dialog-backdrop` wrapping `.tm-spreadsheet-resize-dialog` (no role) | `_spreadsheet.css` L404 fixed, L410 `var(--tm-z-modal)` = **1040** | input keydown only | no | S | **PARTIAL / NOT NAMED.** They are absent from the SS F3 list ("14 dialogů" theory). Only the generic DoD catches them: *"žádný dialog balíčku s vlastním backdropem"*. |
| `TmSpreadsheet.razor` L285–298 | Data-validation in-cell dropdown | Transparent `.tm-spreadsheet-dialog-backdrop--transparent` plus `.tm-spreadsheet-vd` | `_spreadsheet.css` L930 fixed, L939 **1040** | none | no | S (→ `TmOverlayPanel`) | **NOT COVERED** in any SS phase (menu-class) |
| `Data/TmSpreadsheetFilterDropdown.razor` L4–6 | Filter dropdown | Overlay div plus `role=dialog` card | `…FilterDropdown.razor.css` L2/L4 **1000**; L8/L9 **1001** | none | no | M | **COVERED** SS F6: *"Filter dropdown: `MobilePresentation=Auto` z F3 (popover s clampem …)"*, i.e. the core overlay panel |
| `Dialogs/TmSpreadsheetFindReplaceDialog.razor` L4 | Find/Replace | **Non-modal** `role=dialog` panel, `position:absolute` | `…FindReplaceDialog.razor.css` L2–5, **40** | own keydown | no | — | Deliberately non-modal: SS F3 says *"Find/Replace zůstává NEmodální (Q5) … na mobilu nemodální spodní sheet (`TmDrawer` `Modal=false`)"*. Not a rule-9 exception. |

⚠ The five scoped dialogs at hard-coded **1100** already sit above `--tm-z-overlay` 1060, toast 1080 and tooltip 1090. Toasts and tooltips are protected only because they are pinned or promoted.

### 5. Tempo.Blazor.DocumentEditor

Paths are relative to `src/Tempo.Blazor.DocumentEditor/Components/DocumentEditor/`. The trap is `DocumentEditorFocusTrapController.cs` L7 (`internal sealed`), a wrapper around `js/document-editor/focus-management.mjs` (`trapFocus` / `releaseFocusTrap`). "DE plan" means `146c7e1b-e4f9-482c-8c3f-08df2362aa32`, and "DE F5" its phase "Fáze 5: Plovoucí UI — menu toolbaru, dialogy vkládání, field popover, page layout, command palette a dialogy na TmModal (DE-1.4 + DE-1.5 + DE-1.6 + DE-1.7)".

| File | Component | Render mechanism | z-index | Trap | popover? | Effort | Covered by plan? |
|---|---|---|---|---|---|---|---|
| `TmDocumentVersionDialog.razor` L8–13; host `TmDocumentEditor.razor` L1345–1351 | TmDocumentVersionDialog | `<section role=dialog aria-modal>`, inline in the floating root; no scrim on desktop | `_document-editor.css` L958 (not positioned on desktop); only `@media (max-width:40rem)` L2471–2474 `position:fixed; z-index: var(--tm-z-modal)` = **1040** | DocumentEditorFocusTrapController (L67/L90) | no | S | **COVERED** DE F5: *"`TmDocumentVersionDialog`, `TmDocumentCompareDialog` a `TmDocumentDebugModal` (oba debug modaly) přepnout na `TmModal` … smazat `DocumentEditorFocusTrapController.cs`"* |
| `TmDocumentCompareDialog.razor` L13–18; host `TmDocumentEditor.razor` L259–271 (in document flow) | TmDocumentCompareDialog | Same `section` and class `tm-document-version-dialog` | same CSS as above | controller (L97/L141) | no | M | **COVERED** DE F5 (same quote). DE F1 (DE-2.0) first adds a temporary scrim. |
| `TmDocumentJsonDebugModal.razor` L9–16; host `TmDocumentEditor.razor` L244 | TmDocumentJsonDebugModal | `div role=dialog aria-modal` with `__backdrop` and `__content`. **No CSS for `.tm-document-json-debug-modal*` anywhere** (grep: 0), so it is unstyled and in flow. | none | controller (L77/L108) | no | S | **COVERED** DE F1: *"doplnit minimální CSS: `.tm-document-json-debug-modal`, `__backdrop` … CSS je dočasné. Fáze 5 dialogy převede na `TmModal`"*. DE F5 does the migration. |
| `TmDocumentClipboardHtmlDebugModal.razor` L9–16; host L251 | TmDocumentClipboardHtmlDebugModal | same | none | controller (L54/L82) | no | S | **COVERED** DE F1 and DE F5 |
| `TmDocumentCommandPalette.razor` L11–17; host L344 | TmDocumentCommandPalette | Re-uses the **core** CSS `.tm-command-palette-backdrop` but **without** `popover="manual"`. Core `TmCommandPalette.razor` L14 has it and promotes (L275). | `src/Tempo.Blazor/wwwroot/css/components/_command-palette.css` L2 fixed, L5 `var(--tm-z-modal)` = **1040** | controller (L94/L139) | no | M | **PARTIAL / top layer NOT COVERED.** DE F5 says only *"command palette na `TmFocusScope`"* and *"převzít jádrové oddělovače skupin, `kbd`…"*. `TmFocusScope` does not promote. Nothing makes the DE palette a top-layer surface. Fix options: wrap it in `TmModal`, or host the core `TmCommandPalette`. |
| `TmDocumentEditor.razor` L455–503 | Canvas image dialog | `section.tm-document-image-dialog role=dialog aria-modal=true`, inline panel (false aria-modal) | `_document-editor.css` L1777 (grid card, not positioned) | none | no | M (merge with ribbon menu) | **COVERED** DE F5 (DE-1.5): *"spojit menu `tm-document-image-insert-menu` … a dialog (`TmDocumentEditor.razor` ř. 455–503) do jednoho panelu s taby"*, using `TmDocumentFloatingPanel` *"přes jádrový `TmPopover`/`TmOverlayPanel`"* |

**Non-modal floating panels** (`role=dialog` without `aria-modal`, at `--tm-document-z-floating-ui` = `var(--tm-z-popover)` = 1030, set in `_document-editor.css` L11):

- toolbar style gallery L260–268, equation menu L925–932, symbol menu L984–992 and page-layout inspector L1072–1080 in `TmDocumentEditorToolbar.razor`;
- link strip L1853–1861 (`aria-modal="false"`, in flow);
- in `TmDocumentEditor.razor`: token insert panel L402–407, signing popover L504–512 and content-control popover L565–572 (scoped `TmDocumentEditor.razor.css` L147/L152 z 12).

All are **COVERED** by DE F5's `TmDocumentFloatingPanel` subtask: *"Nahradí duplicitní CSS kontejnery `tm-document-style-gallery`, `-equation-menu`, `-symbol-menu`, `-page-layout-inspector`, `__link-dialog`, `__image-dialog`, `__content-control-popover` a `__token-insert-panel`"*, presented *"přes jádrový `TmPopover`/`TmOverlayPanel`"*. `TmOverlayPanel` promotes.

### 6. Tempo.Blazor.Signing

**The package has no dialog-like modal surface.** A grep over `src/Tempo.Blazor.Signing/**/*.razor` for `role="dialog"`, `aria-modal`, `backdrop`, `TmModal`, `TmDialog` and `TmDrawer` returns 0 hits. The only fixed or high-z elements are:

| File | Component | Render mechanism | z-index | Trap | popover? | Effort | Covered by plan? |
|---|---|---|---|---|---|---|---|
| `Components/Signing/TmPdfTemplateDesigner.razor` L204–258 | Designer context actions (own menu) | `div.__context-host` (inline style position) containing `div role=menu` | `_pdf-template-designer.css` L237–239: fixed, `var(--tm-z-tooltip, 1090)` = **1090** | none | no | S | **COVERED** Signing plan `1fba4858`, "Fáze 8: Autorská strana II — inspektor pole a designer desktop + tablet s dotykem": *"kontextové menu jen jednou (odstranit vlastní `__context-actions` vs. skrytý `TmContextMenu`, `.razor` ř. 204–307 …)"*. `TmContextMenu` is a `TmOverlayPanel` and promotes. |
| `TmSigningFormRunner.razor` L47 (`MobilePanelClass`) | Runner mobile panel | in-flow `position: sticky` | `_signing-form-runner.css` L198–201 `var(--tm-z-modal)` **1040** | — | no | — | Not a dialog. Signing "Fáze 9" moves it to a non-modal bottom `TmDrawer`: *"přepnout na `TmDrawer` `DrawerPosition.Bottom` (nemodální, v kontejneru …)"*. |

Others are local in-flow layers only: `_document-comments.css` L129 (3), L372 (`--tm-z-dropdown`, absolute reaction popover and mention list), L509; `_document-page-viewer.css` L126 (1).

**Rule 9's "Signing package dialogs" entry is stale.** There is nothing dialog-like to migrate. The only NP floating surface is the context-action menu, which is already slated for removal.

---

## Recommendation

### A. What belongs in a CORE follow-up plan (core-owned surfaces only)

1. **RTE dialogs: 5 components, effort M, NOT COVERED by any plan. A new core follow-up plan is needed.**
   - Files: `Components/Activity/{Link,Image,Table,Video,FindReplace}Dialog.razor`, their CSS in `wwwroot/css/components/_{link,image,table,video,findreplace}-dialog.css`, and `_rte-dialog-shared.css`.
   - Hosts: `TmRichEditorFull`, `TmRichEditorSimple` and `TmMarkdownEditor`.
   - Move each dialog onto `TmModal` (`Title`, `Footer`, Size Small, `MobilePresentation=Auto`) and delete the five fixed-overlay rules. Today they lack `role`, `aria-modal`, Escape and a trap entirely, so this is an a11y fix as well as a z-order fix.
   - Restore the editor selection and caret on close. Keep the `tm-rte-*` classes used by tests, or migrate them to `data-testid`.
   - Then remove "rich-text editor link/image/table/video/find dialogs" from overlays.md rule 9.
2. **Dashboard widget selector: no new plan needed.** It is already covered by the core Dashboard plan `d254e3dd`, phase 1, subtask B12 (TmModal migration), and phase 6 (redesign). That phase is wave0 with no foundation gate, so it can run immediately.
   - Add a note to B12 that its line refs are stale: the overlay is `_dashboard.css` **L415–425** with `z-index: var(--tm-z-modal)`, not "ř. 378–388, z-index 1000, rgba".
3. **Optional core enabler.** Packages cannot reach `TopLayerInterop` (internal, `InternalsVisibleTo` covers tests only). If any package must keep a bespoke overlay root, for example the DocumentEditor command palette, the choices are a public promote primitive or an `InternalsVisibleTo` for the package assemblies. Otherwise every package fix must go through `TmModal`, `TmDialog`, `TmOverlayPanel` or modal `TmDrawer`.

### B. What to leave to the package plans

| Package | Plan / phases that already migrate it |
|---|---|
| NotionEditor | `c8d4bcd2`: F4 (page header cover and emoji, page info, sidebar bulk, DB record detail, template editor, import/export, comment dialogs); F6 (page search, template gallery, shortcuts, pickers, menus); F7 (share, restrictions, history plus restore confirm, media upload, Tempo-block edit modals) |
| Spreadsheet | `8959eebe`: F1 (temporary CSS for Hyperlink/NameManager/NamedRangeEdit); F3 (all named dialogs plus InsertImage and alerts onto `TmModal`); F6 (filter dropdown onto the core overlay) |
| DocumentEditor | `146c7e1b`: F1 (temporary modality CSS for compare and debug); F5 (version, compare, both debug modals onto `TmModal`; floating panels onto `TmPopover`/`TmOverlayPanel`) |
| Signing | `1fba4858`: F8 (removes the custom context-action menu in favour of `TmContextMenu`). Nothing else to do. |

### C. Gaps: remaining-task notes to add to package plans

1. **Spreadsheet `8959eebe`, SS F3.** Name the column-width and row-height resize dialogs (`TmSpreadsheetCanvasGrid.razor` L80–130, `_spreadsheet.css` L403–411) explicitly in the migration list and theory test. They are only caught by the generic DoD today.
2. **Spreadsheet `8959eebe`, SS F3 or F6.** The data-validation in-cell dropdown (`TmSpreadsheet.razor` L285–298; `.tm-spreadsheet-vd` `_spreadsheet.css` L929–939, z 1040, transparent fixed backdrop) is not covered by any phase. Move it to `TmOverlayPanel` or `TmDropdown`.
3. **DocumentEditor `146c7e1b`, DE F5.** `TmDocumentCommandPalette` re-uses the core `.tm-command-palette-backdrop` without `popover="manual"`. The plan moves only its trap to `TmFocusScope`, which does not promote. Add a subtask to host it in `TmModal`, or reuse core `TmCommandPalette`, so it reaches the top layer.
4. **NotionEditor `c8d4bcd2`, F4 (P3).** The desktop comment popovers `.tm-nbcp` and `.tm-ntcp` (z 1029/1030) stay as today's NP popover under `CommentPresentation=Auto` on desktop. Add a subtask to render the popover variant through `TmOverlayPanel` (promoted) instead of only clamping via `positionFloating`.
5. **NotionEditor `c8d4bcd2`, F7 (N12).** State explicitly that the internal `TmNotionTempoEditorModal` wraps `TmModal` (promoted). Today this is implied only by "na vzoru Fáze 4".
6. **NotionEditor `c8d4bcd2`, F4.** Correct the stale text "Odstranit 3× vlastní overlay (`z-index:1200`)". It is now `var(--tm-z-modal, 1040)` in `TmNotionDbRecordDetail.razor.css` L9, `…DbTemplateEditor.razor.css` L9 and `…DbImportExport.razor.css` L10.
7. **NotionEditor.** `Blocks/TmNotionBlockContextMenu` has its own fixed backdrop and menu (999/1000) and uses `initFocusTrap`. No phase names it; F3 N5 covers it only by its general statement. Add it to F6's list of menus converted to `TmNotionFloatingMenu`.
8. **Docs (overlays.md rule 9).** Drop "the signing package dialogs" from the list: the package has no dialog surfaces. Note that the Notion comment delete confirmations already use `TmDialog`, so they are promoted.

---

## DataTable export rows vs the mockup (icon and description cards)

### Current core markup

- **`src/Tempo.Blazor/Components/DataTable/TmDataTable.razor` L55–80:**
  - `<TmDropdown Text=Export Icon="@IconNames.Download" Class="tm-data-table__export-control" Align=End Offset=4 OnSelect=HandleExportSelectionAsync>`;
  - two `<TmDropdownItem Value="csv|xlsx" Class="tm-data-table__export-item" data-export-format=…>` items carrying **text only**: `@Loc["TmDataTable_ExportCsv"]` = "CSV file" and `TmDataTable_ExportXlsx` = "Excel workbook (XLSX)" (`TmResources.json` L962/L964);
  - no `Icon` on the items and no description.
- **`src/Tempo.Blazor/Components/Dropdowns/TmDropdownItem.razor` L4–15** renders `<button class="tm-dropdown-item" role="menuitem">` with an optional `<TmIcon Size=Sm>` plus `ChildContent`. Its parameters are only `Value`, `Icon`, `ChildContent`, `Class` and `Disabled` (L19–31). There is **no Description slot.**
- **`_data-table.css` L56–58:** `.tm-data-table__export-item { white-space: nowrap; }`. That is the only styling. The menu is a `TmOverlayPanel` (comment at L65–68), so it is already promoted.
- **`src/Tempo.Blazor/Components/ImportExport/TmExportOptions.razor` L6–41** is a separate component with no cards: a title, a `TmCheckbox` entity list (L10–21), a **native `<select class="tm-select">`** for formats (L27–32) and a primary Export `TmButton` (L37–39). Its CSS is `_export-options.css` L1–38.

### Mockup

`output/imagegen/tempo-reimagined/TmDataTable/data-table-export-menu--desktop.png` (also `--tablet` and `--mobile`) shows a primary "Exportovat ▾" button opening a card-like menu. Each row has:

- a file icon (generic document for CSV, green spreadsheet icon for XLSX);
- a bold title: "CSV" / "Excel (XLSX)";
- a muted description line: "Kompatibilní s většinou nástrojů" / "Moderní formát (.xlsx)".

The hovered row has a rounded subtle background.

### Plan coverage, DataTable plan `6288f7e0`

**COVERED** for the DataTable export dropdown (both icon and description), in phase "Fáze 4: Vizuální redesign desktop — TmDataTable, filtry, export, pager a TmViewManager":

- TDD RED subtask: *"`TmDataTableExportTests` (položky CSV/XLSX mají `Icon` a popis formátu)"*.
- Toolbar subtask: *"tři tlačítka Sloupce / Pohledy / Exportovat … export položky `TmDropdownItem.Icon` (`file-text` / ověř v `IconNames.cs` ikonu pro tabulkový soubor, případně dávka ikon F8) + popis (`TmDataTable_ExportCsvDescription`, `_ExportXlsxDescription`)"*.
- UX review subtask against *"`data-table-export-menu--desktop.png`"* and *"…`--tablet.png`"*.
- Localization subtask: *"… export popisy …"*.
- Mobile is in "Fáze 7: Mobilní overlaye": *"Aplikovat `MobilePresentation=Auto` (F3) na export menu (`TmDataTable.razor` ř. 55–80)"* and UX review vs *"`data-table-export-menu--mobile.png`"*.

**Gaps** (the plan does not say these explicitly):

- **How the description is rendered.** It could be extra `ChildContent` markup or a new `TmDropdownItem.Description` parameter (a core API change in `TmDropdownItem.razor`, which has no such slot today).
- **The card or two-line row CSS:** icon tile, title plus muted description, rounded hover background.
- **The title text change** from "CSV file" to "CSV" and from "Excel workbook (XLSX)" to "Excel (XLSX)".

These are left to the UX-review step. Suggest adding them as explicit sub-bullets of the Fáze 4 toolbar subtask.

**`TmExportOptions` is NOT COVERED.** The DataTable plan never mentions it. If the mockup's icon and description cards should also apply to `TmExportOptions` (native `<select>` formats at L27–32), that needs its own task. There is no mockup for `TmExportOptions` in `output/imagegen/tempo-reimagined/`. The export mockups found are for the data table, Notion DB import/export, the email, wireframe and Gantt export dialogs.
