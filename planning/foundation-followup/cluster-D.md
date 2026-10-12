# Cluster D — tokens, contrast, visual a11y, gallery/lightbox, signature, localization, time

Read-only analysis on `main @ 846bc242` (2.9.1 unreleased; `src/Tempo.Blazor/Tempo.Blazor.csproj` `<Version>2.9.1</Version>`, last tag `v2.9.0`).
Strict audit now: `node scripts/audit-css-strict.mjs` → `5853 violation(s), baseline 5853` (ceiling `StrictAuditCeiling = 5862`, `tests/Tempo.Blazor.Tests/Theme/CssTokenAuditTests.cs:21`).
Contrast numbers below were computed with the WCAG formula (same as `ThemeCss.Contrast`), translucent fills composited over `--tm-bg-surface`.

## Cross-cutting rules for the implementer (read first)

1. **Bundle.** Any change under `src/Tempo.Blazor/wwwroot/css/**` must be followed by `dotnet build src/Tempo.Blazor/Tempo.Blazor.csproj` (target `BundleCssFiles`, csproj:169) and the regenerated `src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css` must be committed in the **same** commit (follow-up efdc933b).
2. **Strict audit fails when the count DROPS too.** After removing literals/dark blocks run `node scripts/audit-css-strict.mjs --update-baseline` and commit `scripts/css-token-baseline.json` (the script exits 1 with "the count dropped").
3. **Dark re-declarations.** A `var()`-valued token added to `tokens.css` whose chain reaches a themed token MUST be repeated in `tokens-dark.css` (`TokenContrastTests.Aliases_WhoseChainReachesAThemedToken_AreRedeclaredInDark`, lines 382-444). If the dark expression is **different** from the light one, the token name MUST be added to the `intentionalDifferences` set (TokenContrastTests.cs:419-432) or the "drifted" assertion fails.
4. **`@media` widths.** `tests/Tempo.Blazor.Tests/Layout/media-width-baseline.txt` is an exact, shrink-only list. A new `@media` must use range syntax with a `TmBreakpoints` width (640/768/1024/1280) or the test fails; removing a debt `@media` requires deleting its baseline line. `@container` widths may only be 640/768/1024/1280 (`TmBreakpointsTests.ContainerQueries_UseRangeSyntax_AndOnlyTheFourConstants`).
5. **Resource keys.** en/cs/fr JSON in `src/Tempo.Blazor/Resources/` + both maps in `tests/Tempo.Blazor.Tests/Localization/LocalizationTestBase.cs`. Plural families: en `_One/_Other`, cs `_One/_Few/_Many/_Other`, fr `_One/_Many/_Other`, every form contains `{0` (`PluralLocalizerTests.Every_plural_family_exists_in_all_three_languages…`, line 377). The existing fr `_Many` style is `"{0:N0} de …"`.
6. **[Parameter] changes** → regenerate JSON docs (`dotnet run --project JsonDocumentation/JsonDocumentationGenerator/JsonDocumentationGenerator.csproj -- JsonDocumentation generate`) and commit bundles in the same commit.

---

## 1. [8b3c2021] `--tm-shadow-focus` → `--tm-focus-ring`; `_calendar-view.css` dark blocks; NotionEditor nested-dark aliases

### 1a. Focus-ring migration

**(1) Status: OPEN.** F0 migrated only `.tm-btn*`, `.tm-input*`, `.tm-select*`, `.tm-search-*`, checkbox/radio/toggle, number/decimal error (CHANGELOG.md:349-378). 45 rules still use the glow.

**(2) Current state.** Token definitions (stay — the CHANGELOG restore override `--tm-focus-ring: var(--tm-shadow-focus)` depends on them):
- `tokens.css:214` `--tm-shadow-focus: 0 0 0 3px rgb(from var(--tm-color-primary-500) r g b / 0.4);` ; `:215` `-danger`.
- `tokens-dark.css:149` `--tm-shadow-focus: 0 0 0 3px rgb(from var(--tm-color-primary-400) r g b / 0.5);`
- `tokens.css:385` / `tokens-dark.css:163` `--tm-focus-ring: 0 0 0 2px var(--tm-bg-surface), 0 0 0 4px var(--tm-color-primary);`

Why it matters (measured): glow composited on the surface is **1.62:1** light default, **1.70:1** indigo light, **2.53:1** dark — all under the 3:1 a focus indicator needs. The ring is **5.17:1** light / **5.75:1** dark.

Every remaining usage (rule start–end line, selector). C = core `src/Tempo.Blazor/wwwroot/css/components/`.

| # | File:lines | Selector | Element | `outline: none` in rule? | Class |
|---|---|---|---|---|---|
| 1 | C `_calendar-heatmap.css:180-184` | `.tm-calendar-heatmap__day:focus-visible` (has `outline: var(--tm-border-width) solid …; outline-offset`) | `<button>` 12px cell, 4px gap | no (outline is the indicator) | **H** |
| 2 | C `_code-editor.css:26-29` | `.tm-code-editor__body:focus-within` | container | no | A |
| 3 | C `_data-table.css:596-600` | `.tm-pagination-page-size:focus` | `<select>` (TmPagination.razor:54) | yes | A |
| 4 | C `_data-table.css:841` | `.tm-col-filter-input:focus` | input | yes | A |
| 5 | C `_date-picker.css:30` | `.tm-date-picker-trigger:focus` | `<button>` (TmDatePicker.razor:25, TmDateTimePicker.razor:21) | yes | A |
| 6 | C `_date-range-picker.css:20` | `.tm-date-range-trigger:focus` | `<button>` | yes | A |
| 7 | C `_entity-picker.css:42-45` | `.tm-entity-picker__input:focus` | input | no | A |
| 8 | C `_filterable-dropdown.css:69-73` | `.tm-filterable-dropdown-filter-input:focus` | input | yes | A |
| 9 | C `_findreplace-dialog.css:71-76` | `.tm-rte-findreplace-find-input:focus, …-replace-input:focus` | inputs | yes | A |
| 10 | C `_image-dialog.css:76-81` | `.tm-rte-image-url-input:focus, .tm-rte-image-alt-input:focus` | inputs | yes | A |
| 11 | C `_import-wizard.css:136-140` | `.tm-import-wizard__map-select:focus` (also `border-color: var(--tm-border-color-focus, #6366f1)`) | `<select>` | yes | A (+ drop hex fallback) |
| 12 | C `_link-dialog.css:99-104` | `.tm-rte-link-url-input:focus, .tm-rte-link-text-input:focus` | inputs | yes | A |
| 13 | C `_markdown-editor.css:21-24` | `.tm-markdown-editor.tm-mde-focused` | container (state class) | no | A |
| 14 | C `_masked-text-box.css:34-37` | `.tm-masked-text-box__input:focus` | input | no | A |
| 15 | C `_multiselect.css:22-27` | `.tm-multiselect:focus, .tm-multiselect:focus-within` | combobox div | yes (line 24) | A |
| 16 | C `_multiselect.css:30` | `.tm-multiselect--open` | open state (focus is inside) | no | A |
| 17 | C `_multiselect.css:165-169` | `.tm-multiselect__filter-input:focus` | input | yes | A |
| 18 | C `_multiselect.css:237-240` | `.tm-multiselect__confirm-btn:focus` | button | yes | A |
| 19 | C `_pivot-table.css:332-336` | `.tm-pivot-value-field-editor select:focus` | select | yes | A |
| 20 | C `_pivot-table.css:348-352` | `.tm-pivot-value-field-editor input[type="text"]:focus` | input | yes | A |
| 21 | C `_rich-editor-full.css:16-19` | `.tm-rich-editor-full:focus-within` | container | no | A |
| 22 | C `_rich-editor-simple.css:16-19` | `.tm-rich-editor-simple.tm-rte-focused` | container | no | A |
| 23 | C `_rich-text-editor.css:11-14` | `.tm-rte-wrapper:focus-within` | container | no | A |
| 24 | C `_time-input.css:11-14` | `.tm-time-input:focus-within` | container | no | A |
| 25 | C `_video-dialog.css:71-76` | `.tm-rte-video-url-input:focus, .tm-rte-video-title-input:focus` | inputs | yes | A |
| 26 | C `_signature-capture.css:11-14` | `.tm-signature-capture--invalid .tm-signature-capture__surface` → `--tm-shadow-focus-danger` | persistent invalid state | – | **D** (leave) |
| 27 | `src/Tempo.Blazor.DocumentEditor/wwwroot/css/components/_document-editor-find.css:78-82` | `.tm-document-editor__token-insert-filter:focus-visible` | input | yes | A → DocumentEditor plan 146c7e1b |
| 28 | `src/Tempo.Blazor.EmailTemplates/Components/TmEmailTemplateCanvas.razor.css:91-94` | `.tm-email-canvas-block.is-selected` (`var(--tm-shadow-focus, 0 0 0 2px rgba(37,99,235,.35))`) | selection state | – | **S** → EmailTemplates plan da0a2531 |
| 29 | `src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Embed/TmNotionEmbedBlock.razor.css:172-175` | `.tm-notion-embed-block__source-link:focus-visible` | link | yes | A → Notion plan c8d4bcd2 |
| 30 | `…/NotionEditor/UI/TmNotionAiMenu.razor.css:113-120` | `.tm-notion-ai__textarea:focus, .tm-notion-ai__btn:focus-visible, .tm-notion-ai__tab:focus-visible, .tm-notion-ai__mode-chip:focus-visible, .tm-notion-ai__icon-btn:focus-visible` | mixed | yes | A → Notion |
| 31 | `…/NotionEditor/UI/TmNotionPageSearch.razor.css:145-148` | `.tm-nps__filter-input:focus` | input | no | A → Notion |
| 32 | `…/NotionEditor/UI/TmNotionTemplateGallery.razor.css:91-94` | `.tm-ntg__search:focus-within` | container | no | A → Notion |
| 33 | `src/Tempo.Blazor.Signing/wwwroot/css/components/_condition-builder.css:71-75` | `…__value-input[aria-invalid="true"], …__value-select[aria-invalid="true"]` (`-danger`) | invalid state | – | D → Signing 1fba4858 |
| 34 | `…Signing/…/_formula-builder.css:37-41` | `.tm-formula-builder__textarea:focus` | textarea | yes | A → Signing |
| 35 | `…Signing/…/_formula-builder.css:43-46` | `.tm-formula-builder--invalid .tm-formula-builder__textarea` (`-danger`) | invalid | – | D → Signing |
| 36 | `…Signing/…/_pdf-template-designer.css:167-172` | `.tm-pdf-template-designer__palette-item--active` | selection state | – | S → Signing |
| 37 | `…Signing/…/_recipient-role-editor.css:164-167` | `.tm-recipient-role-editor__email[aria-invalid="true"]` (`-danger`) | invalid | – | D → Signing |
| 38 | `…Signing/…/_signing-field-overlay.css:22-25` | `.tm-signing-field--selected, .tm-signing-field-overlay--selected` | selection state | – | S → Signing |
| 39 | `src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/TmSpreadsheetFormulaBar.razor.css:32-35` | `.tm-spreadsheet-formula-bar__ref:focus` | input | – | A → Spreadsheet 8959eebe |
| 40 | `…TmSpreadsheetFormulaBar.razor.css:74-77` | `.tm-spreadsheet-formula-bar__input:focus` | input | – | A → Spreadsheet |
| 41 | `…TmSpreadsheetSheetTabs.razor.css:95-97` | `.tm-spreadsheet-sheet-tab__input:focus` | input | – | A → Spreadsheet |
| 42 | `src/Tempo.Blazor.Spreadsheet/wwwroot/css/components/_spreadsheet.css:443-446` | `.tm-spreadsheet-resize-dialog__input:focus` | input | – | A → Spreadsheet |
| 43 | `…_spreadsheet.css:603-606` | `.tm-spreadsheet-dialog__input:focus` | input | – | A → Spreadsheet |
| 44 | `…_spreadsheet.css:788-791` | `.tm-fcd__input:focus` | input | – | A → Spreadsheet |
| 45 | `…_spreadsheet.css:1038-1042` | `.tm-dvd__select:focus, .tm-dvd__input:focus` | inputs | – | A → Spreadsheet |

Non-CSS mentions to update when the core is done: `JsonDocumentation/libraryExamples.json:30,44` (brand-theme sample overrides `--tm-shadow-focus`; add/replace with `--tm-focus-ring` there, then regenerate root bundles), `docs/redesign-foundation.md:25` (follow-up table row).

**(3) Exact change.** Rule classes:
- **A — focus of a control/container: 1:1 token swap.** Replace `box-shadow: var(--tm-shadow-focus);` with `box-shadow: var(--tm-focus-ring);`. Box-shadow vs outline semantics are identical (both rings are box-shadows; the ring grows from 3px to 4px outside the box — an ancestor with `overflow:hidden` clips 1px more; `.tm-code-editor__body`/`.tm-rte-wrapper`/`.tm-rich-editor-full` set `overflow:hidden` on **themselves**, which does not clip their own shadow). Keep selectors unchanged (`:focus` stays `:focus`, same as the F0 `.tm-input:focus` precedent).
  - **Forced colors.** In `forced-colors: active` the UA forces `box-shadow: none`; a rule with `outline: none` then has **no** indicator. In every rule of class A that contains `outline: none;`, replace it with `outline: 2px solid transparent;` (precedent: `.tm-radio-segment:has(:focus-visible)` in `_radio-group.css`, bundled line 469 — invisible normally, painted in system colour under forced colors). Rules without an `outline` declaration keep the base `:focus-visible` outline (`base.css:26-29`, 2px `--tm-border-color-focus` at offset 2px) which sits exactly on the ring's outer 2px in the same colour — no visible double ring, no change needed.
  - Rule 11 additionally: `border-color: var(--tm-border-color-focus, #6366f1);` → `border-color: var(--tm-border-color-focus);` (removes baseline entry `color-literal|…_import-wizard.css|border-color: var(--tm-border-color-focus, #6366f1);`).
- **H — heatmap cell (rule 1): NOT a 1:1 swap.** Cells are `--tm-heatmap-cell-size: var(--tm-space-3)` (12px) with a 4px gap (`_calendar-heatmap.css:2,18`); a 4px ring would cover the neighbours. Delete the line `box-shadow: var(--tm-shadow-focus);` (line 183) and change line 181 to `outline: 2px solid var(--tm-border-color-focus);` (keep `outline-offset: var(--tm-border-width);`). The outline already survives forced colors.
- **D — invalid state (`--tm-shadow-focus-danger`): leave as is.** It is a persistent "invalid" glow, not a focus indicator; switching to `--tm-focus-ring-danger` would make an unfocused invalid control look focused. Out of scope; the guard regex must not match `-danger`.
- **S — selection state using the focus glow (28, 36, 38): leave to the owning package plans.** Making a selected item look identical to the focused one is a separate design decision (a selection outline, e.g. `0 0 0 2px var(--tm-color-primary)`). Rule 28 also needs its rgba fallback removed (EmailTemplates plan).
- **Optional same phase (recommended, Q1):** the six F0 ring rules that still pair the ring with `outline: none` get the same forced-colors fix: `_editor-shell.css:85-88, 109-113, 305-308`, `_input.css:175-178` (`.tm-search-clear:focus-visible`), `_overlay-panel.css:97-100`, `_side-panel.css:70-73`; plus `.tm-input:focus` (`_input.css:42-45`, base `.tm-input` has `outline:none` at line 32) and `.tm-btn:focus-visible` (`_button.css:28-30`, base `outline:none` at :24) → add `outline: 2px solid transparent;` inside the focus rule.

Steps (core phase): edit rules 1–25 per class above (26 untouched) → `dotnet build src/Tempo.Blazor/Tempo.Blazor.csproj` → `node scripts/audit-css-strict.mjs --update-baseline` (only rule 11 changes the count, −1) → commit CSS + bundle + baseline + CHANGELOG.

**(4) RED tests.** New `tests/Tempo.Blazor.Tests/Theme/FocusRingAdoptionTests.cs`:
- `CoreComponentStylesheets_DoNotUseTheGlowAsAFocusIndicator`: for every `*.css` in `ThemeCss.CssPath("components")`, strip comments (`ThemeCss.StripComments`), regex `var\(\s*--tm-shadow-focus\s*[,)]` must have **0** matches. RED today: 25 matches in 21 files.
- `PackageStylesheets_GlowUsage_OnlyShrinks`: sweep `src/**/*.css` excluding `src/Tempo.Blazor/`, `obj`, `bin`, `*.bundled.css`; collect `relativePath|selector` for each match; `found.Should().BeSubsetOf(FrozenPackageUsages)` where `FrozenPackageUsages` lists rules 27-32, 34, 36, 38-45 (exact list from the table). A new package usage fails; removing one is allowed (also assert `found.Count <= FrozenPackageUsages.Count`).
- `FocusRingRules_KeepAForcedColorsOutline`: for every rule (`ThemeCss.Rules(file)`) in core components whose selector contains `:focus` (or `tm-mde-focused`/`tm-rte-focused`) and whose body contains `var(--tm-focus-ring`, `ThemeCss.Declares(body,"outline")` must not be `none` (parse value; `none`/`0` fail). RED today for rules 3-6,8-12,15,17-20,25 after the swap if outline:none is kept, and for the 6 F0 rules if Q1 = yes (otherwise give those an explicit allowlist).
- `HeatmapDayFocus_IsAnOutlineOnly`: `ThemeCss.TryProperty("_calendar-heatmap.css", ".tm-calendar-heatmap__day:focus-visible", "box-shadow")` is null and `outline` starts with `2px solid`.
- Keep green: `TokenContrastTests` (unchanged; `FocusRing_HoldsThreeToOne_AgainstTheSurface` already proves the ring), `CssTokenAuditTests`.

**(5) Verification.** `dotnet test tests/Tempo.Blazor.Tests --filter "FullyQualifiedName~FocusRingAdoptionTests|FullyQualifiedName~TokenContrastTests|FullyQualifiedName~CssTokenAuditTests"`; grep `--tm-shadow-focus` in `src/Tempo.Blazor/wwwroot/css/components` → only `_signature-capture.css` (`-danger`). Visual: demo `/pickers`, `/forms`, `/data-table`, `/richtext`, `/import-export` in light/dark/indigo, Tab to each control; Windows High Contrast (or Chromium `--force-renderer-accessibility` + emulate `forced-colors: active` in DevTools) shows an outline.

**(6) Breaking/CHANGELOG.** Visual change → extend the existing "Core controls adopt the two-layer focus ring" entry under `## 2.9.1 (unreleased)` → `### Breaking / Migration` (CHANGELOG.md:349) with a paragraph: "The date pickers, multiselect, filterable dropdown, entity picker, masked text box, time input, pagination page size, column filter, code/markdown/rich-text editors and their dialogs, pivot field editor and import wizard map select now paint `--tm-focus-ring` too. Focus rules that removed the outline keep a transparent 2px outline so Windows High Contrast still shows focus. The calendar heatmap cell drops the glow and keeps a 2px outline." Same restore override applies.

**(7) Effort:** core S (≈0.5 d); each package M-trivial inside its plan.

**(8) Open questions.** Q1 (forced-colors fix of the six F0 ring rules + `.tm-input`/`.tm-btn` in the same phase): (a) yes — one consistent rule, tiny diff **(recommended)**; (b) no — only migrated rules. Q2 (selection states S): (a) leave to package plans **(recommended)**; (b) introduce a core `--tm-selection-ring` token now.

**(9) Scope:** rules 1-25 → **core follow-up plan**. 27 → DocumentEditor 146c7e1b; 28 → EmailTemplates da0a2531; 29-32 → Notion c8d4bcd2 (Fáze 2 tokenizace); 33-38 → Signing 1fba4858; 39-45 → Spreadsheet 8959eebe. Note: `docs/redesign-foundation.md:25` assigns this to "the component's own plan", but most core components here (pickers, multiselect, RTE, pivot, import wizard, heatmap) **have no plan** (plan_list_for_app) — so the core follow-up must own them.

### 1b. `_calendar-view.css` component dark blocks + hex fallbacks

**(1) Status: OPEN** (baseline `scripts/css-token-baseline.json:2138-2147` colour literals, `:2940-2943` component-dark, `:3542` white-on-primary).

**(2) Current state** `src/Tempo.Blazor/wwwroot/css/components/_calendar-view.css` (TmCalendarView, `Components/Pickers/TmCalendarView.razor`):
- 46-50 `.tm-calendar-view__day-cell .tm-cal-day--today { background-color: var(--tm-color-primary-50, #eff6ff); color: var(--tm-color-primary-600, #2563eb); … }`
- 52-55 `… .tm-cal-day--selected { background-color: var(--tm-color-primary-600, #2563eb); color: var(--tm-color-white); }` (white-on-primary violation)
- 68-81 `.tm-calendar-view__event { background-color: var(--tm-color-primary-100, #dbeafe); border-left: 3px solid var(--tm-color-primary-500, #3b82f6); color: var(--tm-text-primary); …}`
- 89-98 `.tm-calendar-view__highlighted { … background-color: var(--tm-color-primary-400, #60a5fa); }`
- 100-104 dark block: `[data-theme="dark"] … .tm-cal-day--today, .tm-dark … { background-color: var(--tm-color-primary-900, #1e3a5f); color: var(--tm-color-primary-300, #93c5fd); }`
- 106-111 dark block: `[data-theme="dark"] .tm-calendar-view__event, .tm-dark … { background-color: var(--tm-color-primary-900, #1e3a5f); color: var(--tm-color-primary-200, #bfdbfe); border-left-color: var(--tm-color-primary-400, #60a5fa); }`
(Fallback `#1e3a5f` is not even the real primary-900 `#1e3a8a`.) Inline consumer colour: `TmCalendarView.razor:60-61` `style="background-color: {e.Color}; border-left-color: {e.Color};"` — unaffected.

**(3) Exact change.** Add to `tokens.css` after line 434 (inside the Calendar picker group):
```css
  /* TmCalendarView. Light/dark pairs live here, not in a component dark block. */
  --tm-calendar-view-today-bg:     var(--tm-color-primary-50);
  --tm-calendar-view-today-fg:     var(--tm-color-primary-600);
  --tm-calendar-view-event-bg:     var(--tm-color-primary-100);
  --tm-calendar-view-event-fg:     var(--tm-text-primary);
  --tm-calendar-view-event-border: var(--tm-color-primary-500);
  --tm-calendar-view-highlight:    var(--tm-color-primary-500);
```
Add to `tokens-dark.css` after line 197:
```css
  --tm-calendar-view-today-bg:     var(--tm-color-primary-900);
  --tm-calendar-view-today-fg:     var(--tm-color-primary-300);
  --tm-calendar-view-event-bg:     var(--tm-color-primary-900);
  --tm-calendar-view-event-fg:     var(--tm-color-primary-200);
  --tm-calendar-view-event-border: var(--tm-color-primary-400);
  --tm-calendar-view-highlight:    var(--tm-color-primary-400);
```
Add all six names to `intentionalDifferences` (TokenContrastTests.cs:419-432).
`_calendar-view.css`: line 47 → `background-color: var(--tm-calendar-view-today-bg);`, 48 → `color: var(--tm-calendar-view-today-fg);`, 53 → `background-color: var(--tm-cal-selected-bg);`, 54 → `color: var(--tm-cal-selected-fg);` (same values primary-600/white, removes white-on-primary), 73 → `background-color: var(--tm-calendar-view-event-bg);`, 74 → `border-left: 3px solid var(--tm-calendar-view-event-border);`, 75 → `color: var(--tm-calendar-view-event-fg);`, 97 → `background-color: var(--tm-calendar-view-highlight);`; **delete lines 100-111**.
Measured: today fg/bg light 4.75 default / 5.62 indigo, dark 5.74/5.73; event text light 14.54/14.49, dark 7.29/7.66. Highlight dot: today light primary-400 is **2.54:1** (default) / 2.98 (indigo) vs white — under 1.4.11's 3:1; primary-500 is 3.68/4.47 (that is a visible light-theme change, Q3). Dark primary-400 vs dark surface 5.75/4.90.
Audit: −10 colour literals, −4 component-dark, −1 white-on-primary → `--update-baseline` (5853 → 5838 if nothing else changed).

**(4) RED tests** (TokenContrastTests.cs):
- `[Theory] [InlineData(false,false)…(true,true)] CalendarViewTodayAndEvents_HoldAa(bool indigo, bool dark)`: with `tokens = indigo ? IndigoTokens(dark) : ThemeCss.TokenGraph(dark)` assert `Contrast(today-fg, today-bg) >= 4.5` and `Contrast(event-fg, event-bg) >= 4.5` and `Contrast(highlight, var(--tm-bg-surface)) >= 3.0`. RED: tokens do not exist (ResolveColour throws/asserts).
- `CalendarView_HasNoComponentDarkBlocksOrHexFallbacks`: `ThemeCss.ComponentCss("_calendar-view.css")` must not contain `data-theme`, `.tm-dark`, or match `#[0-9a-fA-F]{3,8}`; `ThemeCss.Property("_calendar-view.css", ".tm-calendar-view__day-cell .tm-cal-day--selected", "color") == "var(--tm-cal-selected-fg)"`.
- Existing `TmCalendarViewTests` (tests/Tempo.Blazor.Tests/Pickers/) stay green (markup unchanged).

**(5) Verification:** audit + `TokenContrastTests`; demo `/pickers` (TmCalendarView section) light/dark/indigo.
**(6) Breaking?** No API change; dark visuals identical except fallbacks; light dot primary-400→500 only if Q3=a. CHANGELOG `### Changed`: "TmCalendarView colours come from `--tm-calendar-view-*` tokens; the component dark blocks are gone (override the tokens instead of `[data-theme=dark] .tm-calendar-view__event`)."
**(7) Effort:** S. **(8) Q3:** highlight dot (a) primary-500 in light (passes 3:1) **recommended**; (b) keep primary-400 (no visual change, stays 2.54:1). Token naming: `--tm-calendar-view-*` chosen to avoid confusion with the picker `--tm-cal-*` group (`--tm-cal-today-border` was deliberately removed in F0).
**(9) Core follow-up plan** (no Pickers plan exists).

### 1c. NotionEditor `_notion-variables.css` nested-dark aliases + rgba literals

**(1) Status: OPEN** — belongs to the **NotionEditor plan c8d4bcd2, Fáze 2 "Tokenizace celého balíčku"** (separate package `Tempo.Blazor.NotionEditor`, own static assets, references core). Fáze 2 already covers rgba `rgba(59,130,246,…)` (ř. 53, 55), palettes `--tm-notion-option-*`/`--tm-notion-color-*` light+dark, `NotionCssTokenTests`, baseline → 0. It does **not** explicitly cover the nested-dark alias closure described below → add one subtask.

**(2) Current state.** `src/Tempo.Blazor.NotionEditor/wwwroot/css/components/_notion-variables.css` declares everything on `:root` (line 8); `_notion-dark.css:7-59` re-declares literals on `[data-theme="dark"] .tm-notion-editor, .tm-dark .tm-notion-editor`. A `:root` alias computes with light values; the demo puts `data-theme` on a wrapper div (`Demo.SharedUI/Layout/MainLayout.razor:24`), i.e. the nested topology is the demo default.
var() aliases whose chain reaches a token re-declared by core dark/indigo or by `_notion-dark.css`, **not** re-declared in the dark block (computed with a script mirroring `Aliases_WhoseChainReachesAThemedToken…`):
- L88 `--tm-notion-text-default: var(--tm-text-primary)` (0 uses)
- L93-101 `--tm-notion-color-{red,orange,yellow,green,blue,purple,pink,gray,brown}: var(--tm-notion-text-*)` (**36 uses**) — stay the light hex inside a dark editor: brown **2.17:1**, purple 2.49, red 3.23, gray 3.64 on `#191919` (dark values would give 6.6-7.0).
- L109 `--tm-notion-border-drag-over: var(--tm-color-primary)` (0 uses)
- L113 `--tm-notion-checkbox-color: var(--tm-color-primary)` (2 uses)
- L114 `--tm-notion-link-color: var(--tm-color-primary)` (10 uses) — light `#2563eb` on `#191919` = **3.40:1** (dark primary-400 would be 6.92)
- L115 `--tm-notion-link-hover: var(--tm-color-primary-hover)` (4 uses)
- L123 `--tm-notion-toolbar-shadow: var(--tm-shadow-lg)` (0 uses), L127 `--tm-notion-menu-shadow: var(--tm-shadow-xl)` (2 uses)
- L112 `--tm-notion-focus-ring: 0 0 0 2px var(--tm-color-primary-300)` — 0 uses (dead; dark redeclares a literal at `_notion-dark.css:58`).
Literal colours in `_notion-variables.css` (26 baseline entries, `css-token-baseline.json:1550-1575`): rgba L52, 53, 54, 55, 57, 67-75 (9 block bg), 116, 117, 118; hex L79-87 (9 text palette). `_notion-dark.css` adds 73 baseline entries.

**(3) Change (for the Notion plan, Fáze 2 extra subtask):** declare every `var()`-valued `--tm-notion-*` alias on `:root, .tm-notion-editor` (so it recomputes at the editor root where the theme applies) or repeat the same expressions inside the dark block; rename palette literals to `--tm-notion-option-*`/`--tm-notion-color-*-…` data-palette names (exempt by `DATA_PALETTE_DEFINITION`); blue literals → `color-mix(in srgb, var(--tm-color-primary) 10%, transparent)`; delete dead `--tm-notion-focus-ring`, `--tm-notion-text-default`, `--tm-notion-border-drag-over`, `--tm-notion-toolbar-shadow` after confirming 0 uses.
**(4) RED test (Notion plan):** `NotionCssTokenTests.VarAliases_WhoseChainReachesAThemedToken_RecomputeInsideTheEditor` (port of the core closure test over `_notion-variables.css` + `_notion-dark.css`), and a contrast check `--tm-notion-color-*` ≥ 4.5 on `--tm-notion-bg` in dark.
**(5)–(7)** in the Notion plan; effort S on top of Fáze 2. **(8)** none. **(9) Notion plan c8d4bcd2 Fáze 2** — not core.

---

## 2. [421a41ce] Dark `--tm-cal-range-bg` contrast

**(1) Status: OPEN.** `tokens.css:431` and `tokens-dark.css:195` both `--tm-cal-range-bg: var(--tm-color-primary-100);` theme-indigo.css does not touch it.

**(2) Current state.** Used once: `_calendar-grid.css:79` `.tm-cal-day--in-range { background: var(--tm-cal-range-bg); border-radius: 0; }`; day text `.tm-cal-day { color: var(--tm-text-primary); }` (`_calendar-grid.css:67`); classes from `TmCalendarGrid.razor:30,40` (date-range and date-time-range pickers).

Measured in-range text (`--tm-text-primary`) vs fill:

| Fill | default light | indigo light | default dark | indigo dark | fill vs dark surface | fill vs range ends (600) dark |
|---|---|---|---|---|---|---|
| current primary-100 | 14.54 | 14.49 | **1.11** | **1.12** | – | – |
| `--tm-color-primary-subtle` (500 @ .15) | – | – | 11.06 | 11.40 | 1.21 / 1.17 | 2.34 / 1.99 |
| `rgb(from primary-500 / .25)` | – | – | 9.60 | 10.14 | 1.39 / 1.32 | 2.03 / 1.77 |
| **primary-900 (opaque)** | – | – | **9.45** | **10.43** | 1.41 / 1.28 | 2.00 / 1.82 |

Also checked for primary-900: today border (primary-400) on it 4.07 / 3.83 (≥3), other-month text 4.04/4.46 (other-month is `opacity:.5` anyway).

**(3) Change (recommended option c).** `tokens-dark.css:195` → `--tm-cal-range-bg:           var(--tm-color-primary-900);` with a comment "Dark in-range fill: primary-900 holds 9.45:1 (indigo 10.43:1) for --tm-text-primary; the light primary-100 measured 1.1:1." Add `"--tm-cal-range-bg"` to `intentionalDifferences` (TokenContrastTests.cs:419). Light unchanged. primary-900 is already the dark convention for a primary-tinted fill (`--tm-control-hover-fill`, `--tm-status-inprogress-bg`, tokens-dark.css:45,173) and is opaque (no compositing in tests).

**(4) RED test** (TokenContrastTests.cs, next to `CalendarSelectedDay_HoldsAa` line 760):
```csharp
[Theory]
[InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
public void CalendarInRangeDayText_HoldsAa(bool indigo, bool dark)
{
    ThemeCss.Property("_calendar-grid.css", ".tm-cal-day--in-range", "background").Should().Be("var(--tm-cal-range-bg)");
    ThemeCss.Property("_calendar-grid.css", ".tm-cal-day", "color").Should().Be("var(--tm-text-primary)");
    var tokens = indigo ? IndigoTokens(dark) : ThemeCss.TokenGraph(dark);
    ThemeCss.Contrast(ThemeCss.ResolveColour("var(--tm-text-primary)", tokens),
                      PaintedColour("var(--tm-cal-range-bg)", tokens))
        .Should().BeGreaterThanOrEqualTo(4.5, "text dne v rozsahu ({0} {1})", indigo ? "indigo" : "default", dark ? "dark" : "light");
}
```
RED today for both dark cases (1.11 / 1.12). (`PaintedColour` handles option a/b too.)

**(5) Verification:** test green; demo `/pickers` TmDateRangePicker in dark default + dark indigo (select a 5-day range).
**(6) CHANGELOG** `### Breaking / Migration`, next to the selected-day entry (CHANGELOG.md:385, the F0 precedent for calendar fills): "In the dark theme the in-range days of a date-range picker fill with `primary-900` (`--tm-cal-range-bg`); the light `primary-100` put light text on a light fill (1.1:1). Light is unchanged."
**(7) Effort:** S. **(8) Q4:** (a) primary-subtle — matches parent's first suggestion, but only 1.21:1 against the surface (range barely visible); (b) 500 @ .25; (c) **primary-900 (recommended)**.
**(9) Core follow-up plan** — `docs/redesign-foundation.md:26` names a "date-picker plan" that does not exist in plan_list_for_app.

---

## 3. [7800d54b] TmImageGallery items → real buttons + focus restore after TmLightbox close (incl. 835acf83 #2, c51ddf81 #5 "Lightbox Escape focus restore")

**(1) Status: OPEN.** Proof: `TmImageGallery.razor:22` `<div class="tm-gallery-item" @onclick="…">` (no role/tabindex); `GalleryLightboxE2ETests.cs:96-101` only asserts focus left the lightbox.

**(2) Current state.**
- `src/Tempo.Blazor/Components/Gallery/TmImageGallery.razor:17-32` grid; item div with `<img alt="@image.Title">` (null Title → no `alt` attribute at all) and `<div class="tm-gallery-item-title">`.
- `TmLightbox.razor:14-20` renders `TmFocusScope Class="tm-lightbox" popover="manual" Active="true" AriaLabel CloseOnEscape OnEscape="OnClose"`; no restore parameter. Closing unmounts the scope (`@if (IsOpen)`), `TmFocusScope.DisposeAsync` → `FocusTrap.DisposeAsync` → JS `deactivate` → `restoreFocus` (`tm-focus-trap.js:366-389`): order = explicit ElementReference → `data-restore-target` id (`document.getElementById`) → opener captured at activation (`:45`) → body. With a `div` opener nothing focusable was captured. Safari does not focus a clicked `<button>` either, so an explicit id is the robust fix.
- CSS `_image-gallery.css:13-41`; `animations.css:191` lists `.tm-gallery-item` (reduced motion); docs `JsonDocumentation/Components/TmImageGallery.json:56-58` cssClasses.
- Tests: `tests/Tempo.Blazor.Tests/Components/Gallery/TmImageGalleryTests.cs`, `tests/Tempo.Blazor.Tests/Gallery/TmImageGalleryLightboxTests.cs` (use `.Click()` on `.tm-gallery-item` — still valid on a button).

**(3) Exact change.**
TmLightbox (`TmLightbox.razor`):
- add parameter (after line 111):
```csharp
/// <summary>
/// Id of the element that receives focus when the lightbox closes. Null restores the element that
/// held focus when it opened. TmImageGallery passes the thumbnail of the image on screen.
/// </summary>
[Parameter] public string? RestoreFocusTargetId { get; set; }
```
- line 14-20: add `RestoreFocusTargetId="@RestoreFocusTargetId"` to `<TmFocusScope …>`.
TmImageGallery (`TmImageGallery.razor`):
- field `private readonly string _idPrefix = $"tm-gallery-{Guid.NewGuid():N}";` and `private string ItemId(int index) => $"{_idPrefix}-item-{index}";`
- replace lines 22-30 with:
```razor
<button type="button"
        id="@ItemId(i)"
        class="tm-gallery-item"
        aria-label="@(string.IsNullOrWhiteSpace(image.Title) ? Loc["TmImageGallery_OpenImage", i + 1, _imageList.Count] : null)"
        @onclick="async () => await OpenLightboxAsync(image, i)">
    <img src="@(image.ThumbnailUrl ?? image.Url)" alt="@(image.Title ?? string.Empty)" class="tm-gallery-item-img" />
    @if (!string.IsNullOrEmpty(image.Title))
    {
        <span class="tm-gallery-item-title">@image.Title</span>
    }
</button>
```
  No `@onkeydown` (keyboard-activation convention: native button activates itself).
- TmLightbox usage (line 34-45): add `RestoreFocusTargetId="@(_lightboxIndex is int idx ? ItemId(idx) : null)"` — updates while navigating, so close returns to the thumbnail of the image on screen.
- Delete path (`DeleteCurrentAsync`, 116-125): ids are index based, so after removal `ItemId(idx)` is the next image; when the last image was deleted (`idx == _imageList.Count` after removal and Count > 0) set `_focusAfterDelete = _imageList.Count - 1` and in `OnAfterRenderAsync` call `await _itemRefs[_focusAfterDelete].FocusAsync()` (capture `ElementReference`s with `@ref` into a `List<ElementReference>` or use `JS.InvokeVoidAsync("eval",…)`-free `ElementReference`; reset the flag). Empty gallery → nothing to focus (accepted).
CSS `_image-gallery.css` (button reset; replace rule 13-20 and add focus):
```css
.tm-gallery-item {
  position: relative;
  display: block;
  width: 100%;
  padding: 0;
  font: inherit;
  color: inherit;
  text-align: start;
  background: var(--tm-bg-surface);
  cursor: pointer;
  border-radius: var(--tm-radius-md);
  overflow: hidden;
  border: 1px solid var(--tm-border-color);
  transition: box-shadow var(--tm-transition-fast), transform var(--tm-transition-fast);
}
.tm-gallery-item:focus-visible {
  outline: 2px solid transparent;
  box-shadow: var(--tm-focus-ring);
}
```
and in `.tm-gallery-item-title` add `display: block;`.
Resources: `TmImageGallery_OpenImage` en `"Open image {0} of {1}"`, cs `"Otevřít obrázek {0} z {1}"`, fr `"Ouvrir l'image {0} sur {1}"` (place next to `TmImageGallery_NoImages`, en L2752) + both MockTmLocalizer maps (near `TmImageGallery_Counter`, LocalizationTestBase.cs:867 / :4560).
Docs: regenerate JSON (new TmLightbox parameter); add `RestoreFocusTargetId` to `JsonDocumentation/Components/TmLightbox.json` overlay parameters (optional, curated).

**(4) RED tests.**
bUnit `TmImageGalleryTests.cs`:
- `Item_IsANativeButton`: `cut.Find(".tm-gallery-item").TagName == "BUTTON"`, `GetAttribute("type") == "button"`.
- `Item_HasNoEmulatedKeyHandler`: `Assert.Throws<MissingEventHandlerException>(() => cut.Find(".tm-gallery-item").KeyDown("Enter"))`.
- `Item_EnterActivatesExactlyOnce`: per convention dispatch `KeyDown("Enter")` (expect MissingEventHandlerException → wrap in try) then `Click()`; `OnImageClick` counter == 1.
- `Item_WithoutTitle_IsNamedByPosition`: image Title null → `aria-label == "Open image 1 of 2"`, `img` `alt == ""`.
- `Item_Title_IsASpanInsideTheButton`: `.tm-gallery-item-title` TagName `SPAN`.
- `TwoGalleries_HaveDistinctItemIds`.
bUnit `TmImageGalleryLightboxTests.cs`:
- `Lightbox_RestoreTarget_FollowsTheImageOnScreen`: click item 0 → `cut.Find(".tm-lightbox").GetAttribute("data-restore-target") == items[0].Id`; click `.tm-lightbox-next` → equals `items[1].Id`.
- `TmLightbox_WithoutRestoreTarget_RendersNoAttribute` (render TmLightbox directly, IsOpen=true).
E2E `tests/Tempo.Blazor.E2E/GalleryLightboxE2ETests.cs`:
- change lines 96-101: assert `await page.EvaluateAsync<string>("() => document.activeElement?.id")` equals `await items.First.GetAttributeAsync("id")`.
- new `Gallery_Keyboard_EnterOpens_ArrowNavigates_EscapeRestoresToCurrentThumbnail` (1440): `items.First.FocusAsync()`, `Keyboard.PressAsync("Enter")`, lightbox visible, `ArrowRight` → counter contains "2", `Escape` → activeElement id == `items.Nth(1)` id.

**(5) Verification:** `dotnet test --filter "FullyQualifiedName~TmImageGallery"`; E2E class `GalleryLightboxE2ETests` (WASM); manual keyboard pass on `/gallery`; `ComponentDocumentationFreshnessTests`.
**(6) Breaking:** markup change → CHANGELOG `### Breaking / Migration`: "`TmImageGallery` items are `<button type="button" class="tm-gallery-item">` (was a `div`), the title is a `span.tm-gallery-item-title`, and items carry per-instance ids. Host CSS that styled `div.tm-gallery-item` or relied on block children must target the class. Closing the lightbox returns focus to the thumbnail of the image on screen." `### Added`: `TmLightbox.RestoreFocusTargetId`.
**(7) Effort:** S-M. **(8) Q5:** restore target = (a) thumbnail of the image on screen **(recommended**, orientation after browsing); (b) always the opener. **(9) Core follow-up plan** — no Gallery/Lightbox plan exists (plan_list_for_app: 40 plans, none for Gallery).

## 4. [d49c9776] TmLightbox Next button off-screen at 390

**(1) Status: OPEN** (`GalleryLightboxE2ETests` screenshot `tests/Tempo.Blazor.E2E/TestResults/gallery-lightbox/390-lightbox-open.png` shows Next clipped at the right edge).

**(2) Current state** `src/Tempo.Blazor/wwwroot/css/components/_lightbox.css` (Section 42, lines 118-169): `.tm-lightbox-content { display:flex; gap: var(--tm-space-2); max-width: calc(100vw - 2rem) }` (143-147), `.tm-lightbox-image-wrap { flex: 1 }` (148, min-width auto), `.tm-lightbox-img { max-width: 80vw }` (149), buttons `2.5rem` `flex-shrink: 0` (150-155). At 390: 312 (img) + 2×40 + 2×8 = 408 > 358 → Next at x≈384-424. Fits only from 640px up (512+96 = 608 = 640−32).
Note: lines 1-112 are a legacy "Section 24" block for classes **never rendered** (`.tm-lightbox-overlay`, `-container`, `-nav`, `-image-wrapper`, `-image`, `-error`; grep of src/*.razor/*.cs finds none). But three legacy rules still match current markup: `.tm-lightbox-counter` (81-93: the "1 of 8" pill — position absolute/bottom −2.5rem/blur background), and `.tm-lightbox-close`/`-prev`/`-next` which Section 42 overrides (same specificity, later). The legacy `@media (max-width: 768px)` (107-112) is fully overridden and is media debt (`tests/Tempo.Blazor.Tests/Layout/media-width-baseline.txt:20`).

**(3) Exact change.** Append to `_lightbox.css` after line 169:
```css
/* Below 640px the image takes the whole row and the arrows sit over its edges, inside the
   viewport (d49c9776). From 640px up the arrows sit beside the image as before. */
@media (width < 640px) {
  .tm-lightbox-content { width: calc(100vw - 2 * var(--tm-space-2)); max-width: none; }
  .tm-lightbox-image-wrap { min-width: 0; }
  .tm-lightbox-img { max-width: 100%; max-height: 75vh; }
  .tm-lightbox-prev,
  .tm-lightbox-next {
    position: absolute;
    top: 50%;
    transform: translateY(-50%);
    z-index: 1;
    width: var(--tm-touch-target);
    height: var(--tm-touch-target);
  }
  .tm-lightbox-prev { inset-inline-start: var(--tm-space-2); }
  .tm-lightbox-next { inset-inline-end: var(--tm-space-2); }
}
```
(`width < 640px` = `TmBreakpoints.Sm`, range syntax → not media debt.) Recommended in the same step (b32847d0 hygiene): delete legacy lines 1-112 **except** move the counter pill into Section 42: replace line 168 with
```css
.tm-lightbox-counter {
  color: var(--tm-color-white);
  font-size: var(--tm-font-size-sm);
  background: color-mix(in srgb, var(--tm-color-white) 15%, transparent);
  padding: var(--tm-space-1) var(--tm-space-4);
  border-radius: var(--tm-radius-full);
  backdrop-filter: blur(4px);
  white-space: nowrap;
}
```
(the footer already centres it absolutely at `bottom: -2.5rem`), then delete line 20 of `media-width-baseline.txt`, run the audit `--update-baseline` (removes ~10 lightbox literal entries), rebuild the bundle.

**(4) RED tests.** E2E `GalleryLightboxE2ETests`: `[DataRow(390,844)] [DataRow(320,700)] Gallery_Lightbox_NavButtons_StayInsideTheViewport(int w,int h)`: open first item, `next = BoundingBox(".tm-lightbox-next")`, assert `next.X + next.Width <= w` and `prev.X >= 0`, `next.Height >= 44`; then click Next → counter "2". And `Gallery_Lightbox_1440_ArrowsSitBesideTheImage`: `next.X >= img.X + img.Width` (unchanged desktop). bUnit-level CSS guard (Theme folder): `LightboxCss_HasNoLegacySection` — `_lightbox.css` must not contain `.tm-lightbox-overlay`/`-nav`/`-image-wrapper`.
**(5) Verification:** E2E at 320/390/1440; `TmBreakpointsTests`, `CssTokenAuditTests`.
**(6) Breaking:** visual only; removing dead classes `.tm-lightbox-overlay/-container/-nav/-image-wrapper/-image/-error` → CHANGELOG `### Changed` one line (a host that rendered its own markup with those classes loses the styles).
**(7) Effort:** S. **(8)** none. **(9) Core follow-up plan.**

## 5. [b32847d0] TmLightbox 390 polish vs mockups

**(1) Status: OPEN, no reference.** `output/imagegen/tempo-reimagined/` has **no** lightbox/image-gallery mockup (only Notion/diagram/doc-editor "gallery" mockups) and the analysis folder has no "lightbox" mention; no Gallery plan exists.
**(2)** Current 390 state: screenshot above — close button above the image, counter pill below, arrows overflow (fixed by §4).
**(3) Change:** nothing visual beyond §4 until a reference exists. Optional: tokenize the remaining Section 42 literals (`rgba(0,0,0,.85)` backdrop 142, `rgba(0,0,0,.5/.8)` buttons 151/161, `color: white` 152, `rgba(255,255,255,.7)` loading 169) via `color-mix(in srgb, var(--tm-color-black…)` — there is no black token; `--tm-bg-overlay` is 0.5 alpha — so a new `--tm-lightbox-scrim` token would be needed (Q6).
**(4)** E2E screenshots at 390/1440 already exist (`GalleryLightboxE2ETests`) — keep as visual evidence.
**(6)** none. **(7)** S (tokenize) / M (real redesign incl. mockup generation).
**(8) Q6:** (a) close as "no mockup — functional fixes §3/§4 only" **(recommended)**; (b) generate a lightbox mockup (390/1440) during planning and add a polish phase; (c) also tokenize scrim/buttons with a new `--tm-lightbox-scrim` token.
**(9)** Core follow-up plan (only if b/c).

---

## 6. [cb3d590f] Signature overlay clear covers ~13% of the pad at 320

**(1) Status: OPEN** (decision F8-ADDITIVE-EXTENSIONS made Overlay Draw-only; corner overlap not addressed).

**(2) Current state.**
- `TmSignatureCapture.razor:23-32` overlay button rendered first inside `.tm-signature-capture__surface`; text `@Loc["TmSigning_Clear"]` (en "Clear", cs "Vymazat", fr "Effacer").
- `_signature-capture.css:73-84` `.tm-signature-capture--overlay-clear .tm-signature-capture__clear { position:absolute; top: var(--tm-space-2); inset-inline-end: var(--tm-space-2); min-width/min-height: var(--tm-touch-target) }` + shared button padding `var(--tm-space-1) var(--tm-space-3)` (22-42).
- Canvas: SVG `width:100%`, `viewBox 0 0 520 180` (`Width/Height` params, razor.cs:69-72) → at a 320 viewport the pad is ≈238×82 px; button ≈62×44 → 2728/19516 ≈ **14%** (E2E screenshot `TestResults/additive-extensions/signature-overlay-clear-320.png`). A stroke cannot start under the button (it takes the pointerdown) and strokes passing under it are hidden.
- Signing mockup `output/imagegen/tempo-reimagined/Tempo.Blazor.Signing/signing-signature-step--mobile.png` shows the overlay at 390 as a compact ≈44px tile (icon ✕ + small "Vymazat").
- Gates constrain options: a `@media (width < 400px)` would grow the media-debt baseline (not allowed); `@container` may only use 640/768/1024/1280.

**(3) Options.**
- A. **Reserve a corner exclusion** (`padding-inline-end` on the surface = button + 2×space-2 ≈ 78px): zero overlap but the pad loses ~33% width at 320. ✗
- B. **Compact icon-only overlay in a narrow component** (container query on the root, `width < 640px`): button exactly `var(--tm-touch-target)` square, inset `var(--tm-space-1)`, ✕ icon, label kept for AT (visually hidden, not `display:none`). Coverage at 320 ≈ 44×44/19516 = **9.9%**; at 390 (pad ≈ 308×107) ≈ 5.9%. Matches the mockup; CSS + small markup; no API.
- C. **Fall back to the actions row** under 640px via `TmLayout` viewport mode: zero overlap, but contradicts the mockup (overlay at 390) and changes which DOM the button lives in by width.
- D. **Header band**: in overlay mode give the surface a top band (`padding-top: calc(var(--tm-touch-target) + var(--tm-space-1))`) and put the button in it — zero overlap with the drawing area at every width, costs 48px of height.
**Recommendation: B** (Q7), documenting the ≈10% residual at 320; D if the user wants zero overlap.

Exact change for B:
- `TmSignatureCapture.razor:26-31` overlay button body becomes:
```razor
<TmIcon Name="@IconNames.X" Size="IconSize.Xs" />
<span class="tm-signature-capture__clear-label">@Loc["TmSigning_Clear"]</span>
```
  (only in the overlay branch; the actions-row button at 180-185 unchanged).
- `_signature-capture.css` add:
```css
.tm-signature-capture--overlay-clear { container: tm-signature / inline-size; }
@container tm-signature (width < 640px) {
  .tm-signature-capture--overlay-clear .tm-signature-capture__clear {
    top: var(--tm-space-1);
    inset-inline-end: var(--tm-space-1);
    width: var(--tm-touch-target);
    height: var(--tm-touch-target);
    padding: 0;
  }
  .tm-signature-capture--overlay-clear .tm-signature-capture__clear-label {
    position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0;
    overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0;
  }
}
```
  (root is `display:grid`; a size container needs a definite inline size — it fills its block parent, fine; verify the demo column.)

**(4) RED tests.** bUnit `TmSignatureCaptureTests.cs`: `Render_OverlayClear_HasAnIconAndAnAccessibleLabel` — overlay button contains `svg` and `span.tm-signature-capture__clear-label` with text "Clear"; actions-row button (default) has no icon (frozen markup: extend `Render_Default_…` line 13). E2E `AdditiveExtensionsE2ETests.Signature_OverlayClear_SitsInTheSurfaceCorner_WithATouchTarget` (line 245): add `var ratio = button.Width*button.Height / (canvas.Width*canvas.Height)` with `canvas = capture.Locator(".tm-signature-capture__canvas")`; assert `ratio <= 0.10` at 320 (RED today ≈0.14) and `<= 0.07` at 390; assert at 1280 the label is visible (`clientWidth > 1` on the label span) and at 320 the accessible name is still "Clear" (`GetByRole(AriaRole.Button, new(){Name="Clear"})`).
**(5) Verification:** E2E rows 1280/390/320 + screenshots; `TmBreakpointsTests` (container width 640 allowed).
**(6) Breaking:** overlay markup gains an icon + span (opt-in parameter, F8-new) → CHANGELOG `### Changed`.
**(7) Effort:** S. **(8) Q7:** A/B/C/D above, **B recommended**. **(9) Core follow-up plan** (component is core; the Signing plan 1fba4858 phase "Signature step" consumes `ClearButtonPlacement=Overlay` and should just verify).

---

## 7. [ec0299b9 #4] — covered by cluster A (skipped).

## 8. [c88f6e52] + [4c92ba05 #4] Plural demo page with count stepper + E2E

**(1) Status: OPEN** — no localization/plural demo page (`@page "/locali…|/plural"` not found; no `Loc.Plural` in Demo.SharedUI).

**(2) Facts.** Families present in en/cs/fr (guarded by `PluralLocalizerTests`): `TmNotificationBell_NewCount`, `TmModelingIssuePanel_Count`, `TmModelingModelTree_Count`, `TmDocumentEditor_CommentCount`, `TmDocumentEditor_ReviewSummaryComments/Revisions/OpenComments`, `TmWireframeProps_ElementsCount`, `TmScreeningResultPanel_PendingCount`, `TmNotionPageComment_BadgeLabel`, `TmNotionPageHeader_UnresolvedComments`, `Tm_Time_DaysAgo`, `Tm_Time_InDays` (the `TmDiagramProperties_*` ones are not counts). `Loc.Plural` lives in `Tempo.Blazor.Abstractions/Localization/TmLocalizerPluralExtensions.cs` (int/long/double overloads); `TmPluralRules.Select(CultureInfo, double)` is public. Demo culture switch `Demo.SharedUI/Layout/LanguageSwitcher.razor` offers EN/CS only; fr is seeded via `localStorage`/cookie `tm-demo-culture` (pattern `tests/Tempo.Blazor.E2E/Feedback/DialogF2DebtE2ETests.cs:22-43`). Czech rule: 22 is **Other** (not Few; `PluralLocalizerTests.cs:33`); French 0 and 1 are **One**.

**(3) Change.**
- New `src/Tempo.Blazor.Demo.SharedUI/Pages/LocalizationPage.razor`, `@page "/localization"`, `@inject ITmLocalizer Loc`, `@using System.Globalization`. Content (English demo chrome, like other demo pages):
  - Count stepper: `<TmNumberInput Value="_count" ValueChanged="v => _count = v ?? 0" Min="0" Max="10000000" Label="Count" />` inside `<div data-testid="plural-count">`; preset buttons `0 1 2 5 21 22 25 1000000` each `<TmButton data-testid="plural-preset-@n" OnClick="() => _count = n">` (deterministic for E2E).
  - Culture line: `<code data-testid="plural-culture">@((Loc.Culture ?? CultureInfo.CurrentUICulture).Name)</code>`, category `<code data-testid="plural-category">@TmPluralRules.Select(Loc.Culture ?? CultureInfo.CurrentUICulture, _count)</code>`.
  - Table rows for keys `TmModelingIssuePanel_Count`, `TmNotificationBell_NewCount`, `TmDocumentEditor_CommentCount`, `TmWireframeProps_ElementsCount`, `TmScreeningResultPanel_PendingCount`, `Tm_Time_DaysAgo`, `Tm_Time_InDays`: `<td><code>@key</code></td><td data-testid="plural-@key">@Loc.Plural(key, _count)</td>`.
  - Relative-time sample row (optional): `ITmRelativeTimeFormatter` resolved via `[Inject] IServiceProvider`, showing `Format(now.AddDays(-_count))` to show the same counts in time stamps.
- `Demo.SharedUI/Layout/NavMenu.razor` "Design System" section (line 129-133): add `new NavItem("localization", "Localization", "<path … d=\"M3 5h12M9 3v2m1.048 9.5A18.022 18.022 0 016.412 9m6.088 9h7M11 21l5-10 5 10M12.751 5C11.783 10.77 8.07 15.61 3 18.129\" />")`.
- Optional (Q8): add an `FR` button to `LanguageSwitcher.razor` (copy of lines 13-20 with `"fr"`, `data-testid="language-switcher-fr"`).
**(4) RED E2E** `tests/Tempo.Blazor.E2E/Localization/LocalizationPluralsE2ETests.cs : WasmTestBase`, OpenAsync copied from DialogF2DebtE2ETests (seeds culture), `[DataRow]` per (culture, count, expected) for `plural-TmModelingIssuePanel_Count`:
  - en: 0 → "0 issues", 1 → "1 issue", 2 → "2 issues", 5 → "5 issues", 21 → "21 issues", 22 → "22 issues", 25 → "25 issues"
  - cs: 0 "0 nálezů", 1 "1 nález", 2 "2 nálezy", 5 "5 nálezů", 21 "21 nálezů", 22 "22 nálezů", 25 "25 nálezů"
  - fr: 0 "0 problème", 1 "1 problème", 2 "2 problèmes", 5/21/22/25 "N problèmes"
  plus one cs row on `plural-TmNotificationBell_NewCount` count 2 → "2 nová oznámení" and `plural-category` text ("Other"/"One"/"Few") per row. Steps: open `/localization`, click `plural-preset-{n}`, `Expect(...).ToHaveTextAsync(expected)`. Optional bUnit: render page with `UseCzechLocalization()` and assert row text after clicking a preset.
**(5) Verification:** run the E2E class; manual en/cs/fr. Tag Smoke? No (keep full lane).
**(6)** Demo only; no CHANGELOG (or one line under Added "Demo: /localization page").
**(7) Effort:** S. **(8) Q8:** FR in the language switcher (a) yes **(recommended**, fr ships), (b) seed only in tests. **(9) Core follow-up plan** (demo).

## 9. [449c99df] Neutral "Label: {0}" cs counts + Notion relative-time private copies

### 9a. Neutral counts

**(1) Status: PARTLY DONE** (B16 families converted, CHANGELOG "Counted texts are grammatical now"). Remaining keys still use the cs label form and — the stronger argument — **en/fr say "1 replies", "1 pages", "1 events"** (en/fr plural-only templates).

**(2) + recommendation table** (line numbers are en/cs/fr in `src/Tempo.Blazor/Resources/TmResources{,.cs,.fr}.json`).

| Key | en/cs/fr lines | en / cs / fr today | Used at | Recommendation |
|---|---|---|---|---|
| `TmChat_ReplyCount` | 831/831/831 | `{0} replies` / `Odpovědi: {0}` / `{0} réponses` | `TmChat.razor:74, :235` (`string.Format`) | **Plural (core)** |
| `TmAuditLogViewer_EventsCount` | 769/–/– | `{0} events` / `Událostí: {0}` / `{0} événements` | `TmAuditLogViewer.razor:134` | **Plural (core)** |
| `TmDataImport_MoreErrors` | 925/925/925 | `… and {0} more errors…` / `… a další chyby: {0}…` / `… et {0} autres erreurs…` | `TmDataImport.razor:167` | **Plural (core)** — sentence |
| `TmDataImport_ImportPlan`, `_ImportResult`, `_ValidationSummary`, `_ParseSummary`, `_PreviewTruncated` | 922,923,943,926,927 | multi-count sentences; cs label lists ("K importu: {0}, vynecháno: {1}.") | `TmDataImport.razor:195,223,139,56,83` | **Keep** — `Loc.Plural` pluralizes only `{0}`; cs label lists are grammatical |
| `TmDataTable_GroupCount` | 979/978/979 | `{0} items` / `Počet: {0}` / `{0} éléments` | **unused** (group count renders `($"({g.Count})")`, `TmDataTable.razor.cs:1946`) | Keep (dead); optionally deprecate |
| `TmDocumentEditor_WordCount` | 2319/2330/2322 | `{0} words` / `Slova: {0}` / `{0} mots` | `TmDocumentEditorStatusBar.razor:55` | Plural → DocumentEditor plan 146c7e1b (tests `TmDocumentEditorTests.cs:584`) |
| `TmDocumentEditor_PageCount` | 2074/2081/2075 | `{0} pages` / `Stránky: {0}` / `{0} pages` | `TmDocumentEditorStatusBar.razor:56`, `TmDocumentPageNavigator.razor:12` | Plural → DocumentEditor plan |
| `TmRedactionLayer_AreasCount` | 4237/4227/4245 | `{0} redacted areas` / `Začerněných oblastí: {0}` / `{0} zones caviardées` | `Tempo.Blazor.PdfViewer/Components/Files/TmRedactionLayer.razor:10` | Plural → PdfViewer plan 132bf066 |
| `Notion_Audit_Count`, `Notion_Tasks_Count`, `Notion_Blog_Count`, `Notion_Reactions_Count`, `Notion_Space_PageCount`, `TmNotionBlockComment_RepliesCount`, `TmNotionSidebar_PageCount`, `TmNotionTemplateButtonBlock_BlockCount` | 451, 655, 468, 564, 641, 3073, 3837, 3983 | `{0} entries/tasks/posts/reactions/pages/replies/pages/blocks` vs cs `Záznamy:/Úkoly:/Příspěvky:/Reakce:/Stránky:/Odpovědi:/Stránky:/Bloky: {0}` | Notion UI (AuditLogPanel:9, MyTasks:8, BlogList:9, PageReactions:29, SpaceSwitcher:87, BlockCommentPanel:203, Sidebar:193, TemplateButtonBlock:32) | Plural → **Notion plan c8d4bcd2 Fáze 9** (already lists "Neutrální formulace… nahradit Loc.Plural") |
| `ReportServer_Designer_DatasetCount`, `ReportServer_Schedules_CountSchedules/_CountRecentRuns/_CountRuns/_OutboxCount` | 5412, 5441, 5442, 5454, 5462 | `{0} datasets/schedules/recent runs/runs/messages` vs cs labels | `Tempo.ReportServer.Web.Client/Pages/ReportDesignerPage.razor:16`, `SchedulesPage.razor:16,17,116,159` | Plural, low priority → ReportServer backlog (no active plan) |

**(3) Core change (three keys).** Remove the plain key from all three JSON files and add families; call sites → `@Loc.Plural("Key", n)`.
- `TmChat_ReplyCount`: en `_One "{0} reply"`, `_Other "{0} replies"`; cs `_One "{0} odpověď"`, `_Few "{0} odpovědi"`, `_Many "{0} odpovědi"`, `_Other "{0} odpovědí"`; fr `_One "{0} réponse"`, `_Many "{0:N0} de réponses"`, `_Other "{0} réponses"`. `TmChat.razor:74` and `:235` → `<span>@Loc.Plural("TmChat_ReplyCount", ReplyCountOf(…))</span>`.
- `TmAuditLogViewer_EventsCount`: en `"{0} event"/"{0} events"`; cs `"{0} událost"/"{0} události"/"{0} události"/"{0} událostí"`; fr `"{0} événement"/"{0:N0} d'événements"/"{0} événements"`. `TmAuditLogViewer.razor:134` → `@Loc.Plural("TmAuditLogViewer_EventsCount", _totalCount)` (Plural formats the number with CurrentCulture itself).
- `TmDataImport_MoreErrors`: en `_One "… and {0} more error. Download the full report below."`, `_Other "… and {0} more errors. Download the full report below."`; cs `_One "… a ještě {0} chyba. Kompletní report je ke stažení níže."`, `_Few "… a ještě {0} chyby. Kompletní report je ke stažení níže."`, `_Many` = same as `_Few`, `_Other "… a ještě {0} chyb. Kompletní report je ke stažení níže."`; fr `_One "… et {0} autre erreur. Le rapport complet est téléchargeable ci-dessous."`, `_Many "… et {0:N0} d'autres erreurs. Le rapport complet est téléchargeable ci-dessous."`, `_Other "… et {0} autres erreurs. Le rapport complet est téléchargeable ci-dessous."`. `TmDataImport.razor:167` → `@Loc.Plural("TmDataImport_MoreErrors", _validationErrors.Count - MaxDisplayedErrors)`.
- MockTmLocalizer: replace plain entries at `LocalizationTestBase.cs:2234/5577` (TmChat), `:2928/6186` (AuditLog), `:2808/6070` (MoreErrors) with the families (en map gets `_One/_Other`, cs map all four).
**(4) RED tests** (`tests/Tempo.Blazor.Tests/Localization/PluralAdoptionTests.cs`): `[Theory] (1,"1 odpověď") (2,"2 odpovědi") (5,"5 odpovědí")` Czech TmChat reply link; English `(1,"1 reply") (2,"2 replies")`; AuditLog "1 event"; DataImport "… a ještě 1 chyba." — RED today ("Odpovědi: 1", "1 replies"). Grep tests for `"Odpovědi: "`, `"Událostí: "`, `"a další chyby"` and update.
**(6) CHANGELOG** `### Breaking / Migration` add the three keys to the B16 entry's list "no longer exist as a single plain key" (CHANGELOG.md, entry "Counted texts are grammatical now").
**(7)** S. **(8) Q9:** scope: (a) core keys now, packages in their plans **(recommended)**; (b) also do DocumentEditor/PdfViewer one-liners in the core plan (touches files owned by active plans). **(9)** core follow-up (3 keys); rest as listed.

### 9b. Notion relative-time private copies

**(1) Status: OPEN** (Notion package; core is clean).
**(2) Exact locations** (all compute `UtcNow - dt` with hard thresholds, no TimeProvider, no zone):
- `src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionNotificationCenter.razor:289-297` `FormatTime` → keys `Notion_Notifications_JustNow/MinutesAgo/HoursAgo/DaysAgo` (en L533-538); render `:89` `<span class="tm-notification-bell__time">` (no `<time>`).
- `…/Sidebar/TmNotionSidebarRecent.razor.cs:43-57` `GetRelativeTime(DateTime utcTime)` → `TmNotionSidebarRecent_JustNow/MinutesAgo/HoursAgo/DaysAgo` (en L3804-3807); render `TmNotionSidebarRecent.razor:40`.
- `…/Blocks/TmNotionBlock.razor.cs:155-164` `FormatRelativeTime` — **hard-coded English** ("just now", "{n}m ago", `ToString("MMM d")`); render `TmNotionBlock.razor:83`.
- `…/UI/TmNotionBlockCommentPanel.razor.cs:573-581`, `…/UI/TmNotionPageCommentPanel.razor.cs:521-529`, `…/UI/TmNotionTextCommentPanel.razor.cs:443-451` — hard-coded English `static FormatTime`; renders `TmNotionBlockCommentPanel.razor:194,302`, `TmNotionPageCommentPanel.razor:150-151` (already `<time datetime>`), `TmNotionTextCommentPanel.razor:145`.
- `…/Sidebar/TmNotionSidebarTrash.razor.cs:55-66` "Deleted {0} d ago"/"Deleted today"/date — different sentence (keys `TmNotionSidebarTrash_DeletedDaysAgo/_DeletedToday/_DeletedOn` L3813-3815); keep or use `"Deleted {0}"` with the formatter.
- `TmNotionPageComment_Time_*` keys **do not exist** in any resource file; they appear only as dead mock data in `tests/Tempo.Blazor.Tests/Components/NotionEditor/TmNotionPageCommentSectionTests.cs:71-74`.
- Absolute stamps with fixed English formats (same plan, optional): `TmNotionPageHistory.razor.cs:395` (`"MMM d, h:mm tt"`), `TmNotionDbCellCreatedTime/LastEditedTime.razor.cs:12` (`"MMM d, yyyy"`).
**Can Notion use core?** Yes: `ITmRelativeTimeFormatter` is public in Abstractions (`namespace Tempo.Blazor.Localization`), `TmRelativeTimeFormatter` is `public sealed` in core, and `AddTempoBlazorNotionEditor()` already calls `services.AddTempoBlazor()` (`NotionEditorServiceCollectionExtensions.cs:15-21`), which registers the formatter scoped (`ServiceCollectionExtensions.cs:65`). But the safe resolver `TmRelativeTime.Resolve` is **internal** (`src/Tempo.Blazor/Localization/TmRelativeTime.cs:10`; InternalsVisibleTo only `Tempo.Blazor.Tests`), so a package must either `[Inject] ITmRelativeTimeFormatter` (throws in bUnit contexts that registered only `ITmLocalizer`) or copy the fallback `Services.GetService<ITmRelativeTimeFormatter>() ?? new TmRelativeTimeFormatter(Loc, Services.GetService<TimeProvider>())`.
**(3) Core enabler (recommended, core plan, S):** make `TmRelativeTime` `public static` with XML docs (`public static ITmRelativeTimeFormatter Resolve(IServiceProvider services, ITmLocalizer localizer)`), so every package uses one resolver. Notion migration (Notion plan): each copy becomes `(_relativeTime ??= TmRelativeTime.Resolve(Services, Loc)).Format(dt)` + `<time datetime="@dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)" title="@….Absolute(dt)">` (copy the `TmNotificationBell.razor:74, 206-215` pattern); delete keys `Notion_Notifications_*Ago/JustNow`, `TmNotionSidebarRecent_*` (CHANGELOG Breaking like the TmTimeline entry) and the dead mock entries.
**Plan coverage:** Notion plan **Fáze 9** covers it ("odstranit NotionCommentTimeFormatter a privátní kopie času a použít ITmRelativeTimeFormatter … `TmNotionBlock.razor.cs` ř. 155, analytika"), but **Fáze 1** still plans a new interim `NotionCommentTimeFormatter` + `Notion_Time_*` keys — obsolete now that F7 shipped. Recommend amending Fáze 1 to use `ITmRelativeTimeFormatter` directly (and drop `Notion_Time_*`), and listing `TmNotionSidebarRecent` and `TmNotionSidebarTrash` explicitly in Fáze 9.
**(4) RED (core):** `RelativeTimeRegistrationTests.TmRelativeTime_Resolve_IsPublic` (reflection: `typeof(TmRelativeTime).IsPublic`). Notion RED tests per Fáze 9 (`…_UsesTheSharedFormatter` with FakeTimeProvider: cs "před 5 min").
**(6)** Core: `### Added` "`TmRelativeTime.Resolve` is public". **(7)** core S; Notion M (in plan). **(8) Q10:** (a) public `TmRelativeTime.Resolve` **(recommended)**; (b) packages inject the interface and tests register it. **(9)** enabler → core follow-up; migration → Notion plan c8d4bcd2 (amend Fáze 1, Fáze 9).

---

## 10. [37682aee] Remove old `TmTimeline_*` keys "one release later"

**(1) Status: OPEN (by design, not yet due).** The keys were deprecated in **2.9.1, which is still unreleased** (`## 2.9.1 (unreleased)`, csproj `<Version>2.9.1</Version>`, last tag `v2.9.0`). Removing them now would ship deprecation and removal in the same release — a 2.9.0 host overriding `TmTimeline_MinutesAgo` would get no grace period, contradicting CHANGELOG.md:487-507 ("the resources remain so a host that reads them is not broken").
**(2) Inventory for the removal:** keys `TmTimeline_DateFormat/DaysAgo/HoursAgo/JustNow/MinutesAgo` — en `TmResources.json:5069,5070,5074,5076,5079`, cs `:5061,5062,5066,5068,5071`, fr `:5078,5079,5083,5085,5088`; mocks `LocalizationTestBase.cs:820-824, 4517-4521`; tests that reference them: `JsonStringLocalizerTests.cs:73-78` (switch to `Tm_Time_DateFormat`: en "MMM d, yyyy" L5542, cs "d. M. yyyy" L5522), `PluralLocalizerTests.cs:264` (asserts the doc mentions `TmTimeline_MinutesAgo` as deprecated), `F7DocsTests.cs:56` (asserts the *unreleased* changelog section mentions `TmTimeline_MinutesAgo` — after the 2.9.1 release this slice moves anyway), `TmTimelineLocalizationTests.cs:51,64,77` (names only; they test `Tm_Time_*` output); docs `docs/localization-plurals.md:90-93`.
**(3) Recommendation:** do the removal in the **first plan that releases after 2.9.1** (2.10.0 — e.g. a release-prep phase), with a `### Breaking / Migration` entry ("`TmTimeline_*Ago`/`TmTimeline_DateFormat` removed; override `Tm_Time_*`"). Now (core follow-up, S): add a guard so nothing starts reading them again: `RelativeTimeAdoptionTests.DeprecatedTimelineKeys_AreNotReadBySource` — sweep `src/**/*.razor|*.cs` (exclude obj/bin) for `"TmTimeline_(JustNow|MinutesAgo|HoursAgo|DaysAgo|DateFormat)"` → 0 matches (green today; protects the deprecation).
**(6)** Removal = Breaking in 2.10.0. **(7)** S. **(8) Q11:** (a) next-release plan **(recommended)**; (b) now with a Breaking entry (no grace period). **(9)** guard → core follow-up; removal → next release plan.

## 11. [8d6b99e3] Shared core `ITmUserTimeZone` — PRODUCT/API DECISION

**(1) Status: OPEN, design only.** Today (decision F7-POINTER-DRAG-PLURALS-RELATIVE-TIME Q1=A): host registers a scoped `ITmRelativeTimeFormatter` over its own user `TimeProvider` reading a circuit cache (doc snippet `docs/localization-plurals.md:162-175`, compiled by `tests/Tempo.Blazor.Tests/Localization/DocumentedUserZoneRegistration.cs`, compared by `RelativeTimeRegistrationTests`). No library code reads the browser zone.

**(2) Who would consume it.**
- `TmRelativeTimeFormatter` (`_clock.LocalTimeZone`, `TmRelativeTimeFormatter.cs:39,86`) and its 4 core users (TmTimeline, TmActivityTimeline, TmActivityComments, TmNotificationBell) + Notion after 9b.
- `TmScheduler.ResolveTimeZone()` (`TmScheduler.razor:184-193`) falls back to `TimeZoneInfo.Local` (server zone on Server); `DateTime.Today` in `TmScheduler.razor` (2×), `TmSchedulerMonthView.razor`.
- `TmGantt.razor.cs` `DateTime.Today` ×6 (today, `_calendarDate` :760, overdue `:1256`), `TmGanttWorkloadView.razor:13-14`.
- Pickers "today" (`TmDatePicker` 3×, `TmDateRangePicker`, `TmDateTimePicker` 2×, `TmCalendarGrid` 2×, `TmCalendarView` :116-117, `DateTimeHelpers.cs`), `TmCalendarHeatmap`, `TmDeadlineCalculator`, `TmRecurrenceEditor`.
- Scheduler plan 09c5afa6 already plans `TimeProvider` + `ResolveTimeZone()` for the now indicator/`NavigateToday` (analysis plans/scheduler.md:40, 203-226) — benefits but is **not blocked**. Gantt plan cfc9ae8a has no zone work — not blocked either. Neither plan needs it as a gate.

**(3) Design proposal.**
```csharp
// src/Tempo.Blazor.Abstractions/Localization/ITmUserTimeZone.cs  (namespace Tempo.Blazor.Localization)
public interface ITmUserTimeZone
{
    /// <summary>The user's zone. Before the browser reported it: TimeZoneInfo.Local on WebAssembly, UTC on Server/prerender.</summary>
    TimeZoneInfo Zone { get; }
    /// <summary>True once the zone came from the browser (WASM: immediately) or from SetZone.</summary>
    bool IsResolved { get; }
    /// <summary>Raised on the renderer's sync context when Zone changes (browser value arrived or SetZone).</summary>
    event EventHandler? Changed;
    /// <summary>Host override (e.g. a profile setting). Wins over the browser value.</summary>
    void SetZone(TimeZoneInfo zone);
}
```
- Core `TmUserTimeZone` (internal sealed, **scoped**), `OperatingSystem.IsBrowser()` → `Zone = TimeZoneInfo.Local`, `IsResolved = true` (WASM runtime already uses the browser zone; requires `InvariantTimezone` false — default).
- Server/InteractiveAuto-server: a probe reads `Intl.DateTimeFormat().resolvedOptions().timeZone` once per circuit after the first interactive render (new tiny function in an existing core module, e.g. `tm-user-zone.js` export `resolve()`), then `TimeZoneInfo.TryFindSystemTimeZoneById(iana, out tz)` (IANA ids work on .NET 8+ on Windows via ICU) else UTC; raise `Changed`. Probe placement: either `TmLayoutObserver IsViewportScope="true"` does it (already the "outermost layout element" contract) or a new `<TmUserTimeZoneProbe />`; prerender → JS unavailable (`InvalidOperationException`) → stays UTC/unresolved.
- `TmUserTimeProvider : TimeProvider` (internal) — `LocalTimeZone => _zone.Zone` (read **lazily**), `GetUtcNow() => _inner.GetUtcNow()` where `_inner = sp.GetService<TimeProvider>() ?? TimeProvider.System`; timers delegate to `_inner`. **Never registered as `TimeProvider`** (Kestrel/cookie-auth singletons; ValidateScopes).
- Re-render: relative-time consumers subscribe to `Changed` in `OnInitialized` → `InvokeAsync(StateHasChanged)`, unsubscribe in `Dispose` (one small shared helper), or the probe cascades a version value.
- Scheduler: `ResolveTimeZone()` fallback `TimeZoneInfo.Local` → `UserTimeZone?.Zone ?? TimeZoneInfo.Local` (resolved via `Services.GetService`, never bare `[Inject]`).
**Options (Q12):**
- A. **Status quo** — documented host snippet; nothing in core.
- B. **Opt-in core service** — `AddTempoBlazor()` registers `ITmUserTimeZone` (TryAddScoped) and the probe, but the formatter/Scheduler use it only after `services.AddTempoUserTimeZone()` (which replaces the formatter registration with one over `TmUserTimeProvider`). No default behaviour change; the doc snippet becomes a one-liner. **(recommended for the core follow-up; flip the default in 2.10.0)**
- C. **Default-on** — formatter + Scheduler fallback + pickers' "today" use the user zone by default; on Server the default changes from server zone to UTC → browser zone. Biggest correctness win, `### Breaking / Migration` entry, must update `RelativeTimeRegistrationTests` + doc snippet.
**(4) RED tests (for B):** `UserTimeZoneTests` (bUnit, FakeTimeProvider): WASM path `IsResolved` true; server path unresolved → `Zone == Utc`; after probe JSInterop setup `resolve` returns `"America/New_York"` → `Zone.Id` matches, `Changed` raised once; unknown id → UTC; `SetZone` wins over a later browser value; `TmTimeline` with `AddTempoUserTimeZone()` re-renders "yesterday at …" after the zone arrives; `AddTempoBlazor` does **not** register `TimeProvider` (regression of the ValidateScopes trap). E2E Server (7107) `UserTimeZone_Server_FormatsInTheBrowserZone` with Playwright `TimezoneId = "Pacific/Auckland"`.
**(5)** `dotnet test --filter UserTimeZone`; Server/IA demo with a non-local browser zone.
**(6)** B: Added only. C: Breaking. **(7)** B: M (≈2 d); C: L. **(8)** Q12 above. **(9)** Core follow-up (B) — but it is a product/API decision; can also stay "recorded, not scheduled" (A).

## 12. [4c92ba05 #3] Optional live-refresh ticker for relative-time stamps

**(1) Status: OPEN (documented gap).** `docs/localization-plurals.md:182-184` "No live refresh … the host re-renders".
**(2)** Consumers: TmTimeline.razor:49, TmActivityTimeline.razor:73-74, TmActivityComments.razor:35-36, TmNotificationBell.razor:74 (all compute at render time). Precedents for timers on the injected clock: `TmSearchInput.razor:62-77,151-172` (`Clock.CreateTimer`, `InvokeAsync`), Scheduler plan's `TmSchedulerNowIndicator` (self-re-rendering, 60s).
**(3) Options (Q13):**
- A. Keep documented "no live refresh" **(recommended for now — no request from a consumer, adds timers to every circuit)**.
- B. Per-component parameter `RelativeTimeRefresh` (TimeSpan?) on each of the 4 components — 4 new parameters, parent re-renders whole lists.
- C. Shared scoped `TmRelativeTimeTicker` (one `ITimer` per circuit on the registered `TimeProvider`, 60s, runs only while subscribers exist) + opt-in `TmRelativeTimeOptions.LiveRefresh` (IOptions, default false); consumers subscribe only while showing a stamp < 1 h old.
- D. New public `<TmRelativeTime Value="…" />` component rendering the `<time datetime title>` element and re-rendering **only itself** with an adaptive interval (<1 min: 15 s, <1 h: 60 s, same day: next full hour, else none) driven by C's ticker; the 4 components use it internally. Best long-term shape (also removes the 4 copies of the `<time>` markup); M.
If wanted later: D on top of C, opt-in, tests with `FakeTimeProvider.Advance(TimeSpan.FromMinutes(1))` asserting "5 min ago" → "6 min ago" re-render count = 1 for the stamp only.
**(6)** Added (opt-in). **(7)** A: 0; C+D: M. **(9)** backlog / core follow-up only if Q13 ≠ A.

## 13. [4c92ba05 #5] Confirm TmNotificationBell has no private relative-time code

**(1) Status: ALREADY DONE.** Proof: `src/Tempo.Blazor/Components/Notifications/TmNotificationBell.razor:74` `<time … datetime=… title="@AbsoluteTime(n.CreatedAt)">@FormatTime(n.CreatedAt)</time>`; `:206-215` `[Inject] IServiceProvider Services`, `_relativeTime ??= TmRelativeTime.Resolve(Services, Loc)` for `Format`/`Absolute`; the only "ago" literal is the XML comment at `:210`. A sweep of `src/**` for `TotalMinutes <`/`TotalHours <` thresholds finds **only** NotionEditor files (§9b) and unrelated chart code. `TmNotificationToastContainer.razor` renders no timestamps. Covered by `RelativeTimeAdoptionTests.cs:148` (bell). Nothing to do; Notion copies → §9b.

## 14. [835acf83 #2] — covered by §3 (focus restore / buttons) and §4 (Next off-screen).

---

## Open questions for the user (summary)

| # | Question | Options | Recommendation |
|---|---|---|---|
| Q1 | Fix forced-colors (`outline:none` → `2px solid transparent`) also in the six F0 ring rules + `.tm-input:focus`, `.tm-btn:focus-visible`? | yes / no | **yes** |
| Q2 | Selection states that reuse the focus glow (EmailTemplates, Signing) | package plans / new core `--tm-selection-ring` | **package plans** |
| Q3 | TmCalendarView highlight dot in light | primary-500 (3.68:1) / keep primary-400 (2.54:1) | **primary-500** |
| Q4 | Dark `--tm-cal-range-bg` | primary-subtle / 500@.25 / primary-900 | **primary-900** (9.45/10.43:1, opaque) |
| Q5 | Lightbox close restores focus to | thumbnail of image on screen / original opener | **image on screen** |
| Q6 | Lightbox visual polish (no mockup exists) | close as functional-only / generate mockup + polish phase / also tokenize scrim | **functional-only** |
| Q7 | Signature overlay coverage at 320 | A exclusion / B compact icon-only (<640 container) / C actions fallback / D header band | **B** (≈10% residual) or D for zero overlap |
| Q8 | FR button in demo language switcher | yes / test-seed only | **yes** |
| Q9 | Neutral-count conversion scope | core 3 keys now, packages in plans / all one-liners in core plan | **core now, packages in plans** |
| Q10 | Make `TmRelativeTime.Resolve` public for packages | public / inject interface | **public** |
| Q11 | Remove `TmTimeline_*` keys | next release plan (2.10.0) / now (no grace) | **next release**; add guard now |
| Q12 | `ITmUserTimeZone` | A status quo / B opt-in service / C default-on | **B**, default in 2.10.0 |
| Q13 | Live-refresh ticker | none / per-component param / shared ticker / `<TmRelativeTime>` | **none for now** (later C+D) |

## Summary table

| Item | Status | Effort | Breaking / CHANGELOG | Owner |
|---|---|---|---|---|
| 8b3c2021a focus ring, core rules 1-25 (+heatmap special) | OPEN | S | Breaking/Migration (visual) — extend F0 focus-ring entry | **Core follow-up** |
| 8b3c2021a package rules 27-45 | OPEN | S each | in each plan | DocumentEditor 146c7e1b, EmailTemplates da0a2531, Notion c8d4bcd2 F2, Signing 1fba4858, Spreadsheet 8959eebe |
| 8b3c2021b `_calendar-view.css` → 6 `--tm-calendar-view-*` tokens | OPEN | S | Changed | **Core follow-up** |
| 8b3c2021c Notion nested-dark aliases + literals | OPEN | S (in F2) | in Notion plan | Notion plan c8d4bcd2 Fáze 2 (add alias-closure subtask) |
| 421a41ce dark `--tm-cal-range-bg` (1.11:1 → 9.45:1) | OPEN | S | Breaking/Migration (dark visual) | **Core follow-up** (no picker plan exists) |
| 7800d54b gallery buttons + focus restore (`TmLightbox.RestoreFocusTargetId`) | OPEN | S-M | Breaking (markup) + Added | **Core follow-up** (no Gallery plan) |
| d49c9776 lightbox Next off-screen (+ dead legacy CSS) | OPEN | S | Changed | **Core follow-up** |
| b32847d0 lightbox polish vs mockups | OPEN, no mockup | S/M | – | Core follow-up only if Q6 b/c |
| cb3d590f signature overlay coverage | OPEN | S | Changed | **Core follow-up** |
| ec0299b9 #4 | – | – | – | cluster A |
| c88f6e52 + 4c92ba05 #4 plural demo `/localization` + E2E | OPEN | S | (demo) | **Core follow-up** |
| 449c99df neutral counts — core 3 keys | PARTLY DONE | S | Breaking (keys) | **Core follow-up** |
| 449c99df neutral counts — packages | OPEN | S each | in plans | DocumentEditor, PdfViewer, Notion F9, ReportServer backlog |
| 449c99df Notion relative-time copies | OPEN | core S (public `TmRelativeTime`), Notion M | Added (core) / Breaking keys (Notion) | enabler core; migration Notion plan (amend F1, F9) |
| 37682aee remove `TmTimeline_*` | OPEN, not due (2.9.1 unreleased) | S | Breaking in 2.10.0 | guard → core; removal → next release plan |
| 8d6b99e3 `ITmUserTimeZone` | OPEN, decision | M (B) / L (C) | Added (B) / Breaking (C) | Core follow-up if Q12 = B/C |
| 4c92ba05 #3 live refresh | OPEN, optional | 0 / M | Added (opt-in) | backlog |
| 4c92ba05 #5 bell private code | **ALREADY DONE** | – | – | – |
| 835acf83 #2 | covered by 7800d54b + d49c9776 | – | – | Core follow-up |
