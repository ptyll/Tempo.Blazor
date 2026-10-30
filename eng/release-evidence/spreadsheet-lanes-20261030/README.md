# Spreadsheet E2E regression lanes — phase 7d (2026-10-30)

Hosts: API https://localhost:5100 + WASM https://localhost:7106 started by the implementer,
`TM_E2E_SELF_HOST=false`. Each lane is one `dotnet test --filter` batch over the
`Tempo.Blazor.E2E` project (`--no-build`), run sequentially on this box.

## phase3

- filter: `Name~Phase3_`
- duration: 2997s (exit 0)
- result: Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 49 m 50 s - Tempo.Blazor.E2E.dll (net10.0)

## phase4

- filter: `Name~Phase4_`
- duration: 2461s (exit 1)
- result: Failed!  - Failed:     1, Passed:    15, Skipped:     0, Total:    16, Duration: 40 m 53 s - Tempo.Blazor.E2E.dll (net10.0)
- failed tests:
  - Phase4_TextToColumns_Steps1to3_AllSizes

## phase5

- filter: `Name~Phase5_`
- duration: 1076s (exit 0)
- result: Passed!  - Failed:     0, Passed:    26, Skipped:     0, Total:    26, Duration: 17 m 47 s - Tempo.Blazor.E2E.dll (net10.0)

## phase6-responsive

- filter: `Name~Phase6_`
- duration: 1808s (exit 1)
- result: Failed!  - Failed:     2, Passed:    23, Skipped:     0, Total:    25, Duration: 30 m - Tempo.Blazor.E2E.dll (net10.0)
- failed tests:
  - Phase6_Desktop_ContextMenu_Keyboard_RovesAndEscapeCloses
  - Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView

## phase7-touch

- filter: `Name~Phase7_`
- duration: 903s (exit 0)
- result: Passed!  - Failed:     0, Passed:    11, Skipped:     1, Total:    12, Duration: 14 m 57 s - Tempo.Blazor.E2E.dll (net10.0)

## phase7c

- filter: `Name~Phase7c_`
- duration: 408s (exit 0)
- result: Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 6 m 42 s - Tempo.Blazor.E2E.dll (net10.0)

## phase2

- filter: `Name~Phase2_`
- duration: 925s (exit 0)
- result: Passed!  - Failed:     0, Passed:    22, Skipped:     0, Total:    22, Duration: 15 m 19 s - Tempo.Blazor.E2E.dll (net10.0)

## phase6-class

- filter: `FullyQualifiedName~Tempo.Blazor.E2E.SpreadsheetPhase6E2ETests.`
- duration: 509s (exit 0)
- result: Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 8 m 23 s - Tempo.Blazor.E2E.dll (net10.0)

## baselines

- filter: `ClassName~SpreadsheetBaselineScreenshots|ClassName~SpreadsheetPhase2BaselineScreenshots|ClassName~SpreadsheetPhase3BaselineScreenshots|ClassName~SpreadsheetPhase4BaselineScreenshots|ClassName~SpreadsheetPhase5BaselineScreenshots|ClassName~SpreadsheetPhase6BaselineScreenshots`
- duration: 7s (exit 0)
- result: <no summary line — see logs/baselines.log>

## base

- filter: `FullyQualifiedName~Tempo.Blazor.E2E.SpreadsheetE2ETests.&Name!~Phase`
- duration: 8419s (exit 0)
- result: Failed!  - Failed:     5, Passed:   129, Skipped:     0, Total:   134, Duration: 2 h 1 m - Tempo.Blazor.E2E.dll (net10.0)
- failed tests:
  - CanvasJsEngine_InlineFormulaEditorArrowUpDownKeepSessionAndDoNotChangeActiveCell
  - BenchmarkPage_RunsCanvasBenchmark
  - StatusBar_ZoomIn_EnlargesCellsAndUpdatesPercent
  - TextToColumns_SplitsSingleColumnIntoThree
  - PasteSpecial_Transpose_SwapsRowToColumn


## What the lanes found (and what was fixed)

The first pass surfaced **two genuine product bugs** and one test-side debt; all three were fixed
test-first and the failing specs were re-run:

| # | Symptom | Root cause | Fix | RED test |
|---|---|---|---|---|
| 1 | `AutoFilter_*` (4 specs) and every dialog spec whose open path runs `EnableFilterAsync` waited 30 s on `filterButtons.length > 0` — "the Filter toggle does nothing" | `drawFilterButtons` pushed each funnel with `columnWidth: read(colFrame, "Width", …)`, but `colFrame` does not exist in that scope (it belongs to `drawSelectionOverlay`). The `ReferenceError` aborted the whole content paint **inside its requestAnimationFrame**, after `state.filterButtons` had been cleared — invisible in the DOM | pass the drawn cell's own `w` as the column width, and hand the resolver the funnel's OWN box (`boundsTop` / `boundsHeight`) so the coarse-pointer hit rect stays on the funnel's row instead of jumping into the column-header gutter | `spreadsheet-canvas-filter-buttons.test.mjs` (`npm run test:spreadsheet`): 3 new tests, 55 pass / 0 fail |
| 2 | `Phase6_Desktop_ContextMenu_Keyboard_RovesAndEscapeCloses`: the menu opened, `format-cells` never had the focus → ArrowDown/Home/End/Escape dead | the grid's focus restore is **probe-then-focus** (`isFocusOutside` → `ElementReference.FocusAsync`). The host-owned menu focuses its first entry from its own `OnAfterRenderAsync` in the same batch, so the probe answered "still in the grid" and the focus hop landed ~25 ms later and stole the caret back | one atomic JS guard `tmSpreadsheetCanvas.focusGridUnlessFocusMovedAway` (check AND focus in the same task) + `TmSpreadsheetCanvasGrid.SuppressFocusRestore`, fed by the host's `IsHostSurfaceFocused` (open context menu / resize dialog) | `TmSpreadsheetContextMenuFocusTests` (4 tests, bUnit): red before, green after |
| 3 | 5 base-class specs timed out in the **page boot** (`WaitForAppReadyAsync`: 89 s budget → the fallback wait even got `Timeout = 1ms`) | the box ran a second agent's test suites concurrently (load average ≈ 15 on 12 cores); every failure is a navigation/boot timeout, none is an assertion | re-ran each alone: `CanvasJsEngine_InlineFormulaEditorArrowUpDown…`, `BenchmarkPage_RunsCanvasBenchmark`, `StatusBar_ZoomIn_EnlargesCellsAndUpdatesPercent`, `TextToColumns_SplitsSingleColumnIntoThree`, `PasteSpecial_Transpose_SwapsRowToColumn` — **all five pass** | — (flake, proven by isolated runs) |

Test-side debt fixed with them (all inside `tests/Tempo.Blazor.E2E`, no product contract touched):

- **`SpreadsheetChromeHelpers.cs`** — one chrome-aware command opener (`data-command-id` on a compact row,
  the row's "More" when the command does not fit, the desktop tab + `button[title]` on desktop) plus
  `OpenOnDesktopThenResizeAsync` / `OpenPhase3DialogThenResizeAsync` for the specs whose subject is the
  PHONE presentation of a surface that only the desktop chrome can open (G2 keeps the advanced commands
  desktop-only). `ClickTabButtonAsync`, `EnableFilterAsync` and `SetWholeNumberValidationAsync` now route
  through it, so the three long-red dialog specs and the validation-alert arms pass at 390/320.
- **`Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView`** no longer reports `Inconclusive`:
  it resets the seeded Notion store (`POST /api/notion/reset`, as `SpreadsheetBlockE2ETests` does), opens
  the modal through the real "Create Spreadsheet" entry point of the empty block, narrows to 390 × 844 and
  568 × 320 and asserts `data-layout="mobile"` plus the Save / Discard footer inside the viewport. No demo
  seed data was added.
- **`Phase4_TextToColumns_Steps1to3_AllSizes`** opens the wizard on the desktop chrome and narrows, like the
  other three-viewport specs.
- **Context-menu focus spec** waits for the roving focus instead of reading `activeElement` in the same tick
  as "the menu is visible".

### Re-runs after the fixes (all green)

| Spec | Lane it belongs to | Result |
|---|---|---|
| `Phase3_*` (16) | phase3 | 16/16 (already green after the filter fix) |
| `Phase4_TextToColumns_Steps1to3_AllSizes` | phase4 | passed (the only phase-4 failure) → phase4 = 16/16 |
| `Phase6_Desktop_ContextMenu_Keyboard_RovesAndEscapeCloses` | phase6-responsive | passed |
| `Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView` | phase6-responsive | passed → phase6-responsive = 25/25 |
| the 5 base-class boot timeouts | base | **129 of 134 in the lane** + these 5 measured ALONE afterwards (not a re-run of the class): `CanvasJsEngine_InlineFormulaEditorArrowUpDownKeepSessionAndDoNotChangeActiveCell` 44 s, `BenchmarkPage_RunsCanvasBenchmark` 58 s, `StatusBar_ZoomIn_EnlargesCellsAndUpdatesPercent` 43 s, `TextToColumns_SplitsSingleColumnIntoThree` 53 s, `PasteSpecial_Transpose_SwapsRowToColumn` 48 s — 5/5 pass, so every one of the 134 has a green run on this tree |

**Scope of the re-runs, stated plainly:** `phase4` and `phase6-responsive` were **NOT re-run as whole
classes** after the fixes — only their failing specs were re-run (`Phase4_TextToColumns_Steps1to3_AllSizes`;
`Phase6_Desktop_ContextMenu_Keyboard_RovesAndEscapeCloses` and
`Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView`), each green. Their lane numbers above are
therefore the ORIGINAL run's counts plus the isolated re-runs of the fixed specs, not a fresh class-wide
summary; a whole-class re-run belongs to the phase-8 full gate.

Supporting lanes re-run after the product fixes: `npm run test:spreadsheet` 55/55,
`Tempo.Blazor.Tests.Components.Spreadsheet` 1083/1083, `Tempo.Blazor.Tests.Wireframe` 1556/1556,
`JsonDocumentation validate --fail-on-drift` clean.

## The block lane and the shared `/api/notion/reset`

`SpreadsheetBlockE2ETests` is now a lane of `run-lanes.sh` too
(`run_lane block "FullyQualifiedName~Tempo.Blazor.E2E.SpreadsheetBlockE2ETests."`) — it was missing from the
first pass and is the lane that resets the demo Notion store on every test.

| Run | Command | Result |
|---|---|---|
| block lane (first run, right after the hosts restarted) | `logs/block.log` | 3 passed / 3 FAILED, 8 m 32 s — all three failures are the same **89 s `WaitForAppReadyAsync` boot timeout** (cold WASM start under load), no assertion ran |
| the same three specs alone | `logs/block-retry.log` | 3/3 passed, 1 m 54 s |
| **back-to-back, one batch** — the Notion edit-modal spec + the whole block class | `logs/notion-shared-reset.log` | **7/7 passed, 4 m 25 s** → the shared `POST /api/notion/reset` does not interfere: the edit-modal spec resets the store, then every block test resets it again and still finds its seeded block |

## Baselines (policy decision `SPREADSHEET-BASELINE-POLICY`)

The six `*BaselineScreenshots` classes are **skipped by design** (`BaselineGeneratorTestBase`,
`TM_WRITE_BASELINES` gate, category `BaselineGeneration`, root `artifacts/baseline/`) — 22 skipped, 0 failed.
The tracked PNGs under `tests/Tempo.Blazor.E2E/__screenshots__/spreadsheet` are written by ORDINARY specs on
every run, so this pass refreshed **127 of 163** files. Per the decision they are the new truth and are
committed with this phase, but only after a visual review — the reviewed samples:

- `phase2-tablet-1024-touch--comfortable.png` — two compact rows (no tab strip), Comfortable row heights,
  gutter labels, status bar with zoom: matches the reference direction of
  `output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/spreadsheet--tablet.png` (the reference's
  document header is a host opt-in, SP3, so its absence is by design).
- `phase3-custom-filter--desktop.png`, `phase3-data-validation--desktop.png` — the funnels are back
  (the AutoFilter fix), the dialog content is unchanged.
- `phase4-sort-six-levels--desktop.png` — six levels, `Add level` outlined, headers / case-sensitive
  stacked in the right column.
- `phase3-format-cells--mobile-320.png` — fullscreen panel, category select above the options, footer inside
  the viewport.
- `phase6-mobile-390-context-sheet-after-scroll.png` — the phone sheet over the scrolled grid.
- `name-manager-dialog-desktop.png` — the foreground dialog is identical to the committed one; the delta is
  the blurred page BEHIND the scrim (the demo page's scroll position and section set drift between runs),
  which is the context noise the decision accepts.

## Review round (UX approved; code-reviewer + architect ChangesRequested)

All findings fixed:

| # | Finding | Fix |
|---|---|---|
| 1 (Medium) | the `SpreadsheetBlockE2ETests` lane was missing from the evidence | `run_lane block "FullyQualifiedName~Tempo.Blazor.E2E.SpreadsheetBlockE2ETests."` added to `run-lanes.sh`; lane run (3/6 — all three failures the same 89 s cold-boot timeout right after the host restart, 3/3 green on retry), and the **Notion edit-modal spec + the whole block class run back-to-back in one batch: 7/7 in 4 m 25 s**, proving the shared `POST /api/notion/reset` does not interfere. See the section above. |
| 2 (Low) | `SpreadsheetChromeHelpers` matched the "More" entry by substring and dispatched synthetic clicks | `FindMenuEntryAsync` prefers `data-command-id`, else an ANCHORED exact-label regex (`^\s*{title}\s*$`) — so "Sort ascending" can never select a "Custom sort…" row; the row command / trigger / entry are all clicked for real, and `DispatchEventAsync` survives only as the documented fallback for a sticky bar repositioning under Playwright's actionability check (`ClickMoreTriggerAsync`). |
| 3 (Low) | the `isFocusOutside` JSDoc had drifted onto the new helper; a stale XML comment on `_forceFocusAfterRender` | the doc moved back to `window.tmSpreadsheetCanvas.isFocusOutside` and now says it is kept for compatibility only (the grid uses `focusGridUnlessFocusMovedAway`); the C# comment names the atomic guard + `SuppressFocusRestore`. |
| 4 (Low) | the schema prop had no parity guard | `StencilPackRegistryParityTests.SpreadsheetStencil_ExposesLayoutMode` (Enum, default `auto`, options `auto|desktop|tablet|mobile`, native renderer) — green. |
| 5 (Low) | the README over-claimed the base lane and hid the re-run scope | exact accounting (129 in the lane + the 5 isolated passes with their times: 44 s / 58 s / 43 s / 53 s / 48 s) and an explicit note that `phase4` and `phase6-responsive` were NOT re-run as whole classes — only their failing specs, each green. |
| 6 | the two decisions | `SPREADSHEET-BASELINE-POLICY` and `SPREADSHEET-E2E-COMMAND-PATH` both exist and are Active; the second one's text was patched to describe the exact-match/real-click behaviour it now guarantees. |
| 7 | wireframe stencil screenshots at desktop/tablet/mobile | **deferred to phase 8**: the stencil is covered by bUnit (5 renderer tests + the parity test), but an E2E capture needs a demo host that renders the three variants side by side plus a tracked baseline — not cheap at the end of this phase, and phase 8 is the documentation/screenshot phase. |

Verification after the review fixes: `npm run test:spreadsheet` 55/55; bUnit
`Components.Spreadsheet|Wireframe|Stencil` 2734 passed / 1 failed
(`TmWireframePagesPanelTests.Move_ViaMenu_UpAndDown` — passes alone and as its whole 37-test class, so it is an
xUnit parallel-collection flake, not a regression: that component is untouched by this phase); the E2E project
rebuilds clean.
