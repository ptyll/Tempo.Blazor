# Spreadsheet redesign — final phase evidence (2026-10-30)

Phase 8 of plan 8959eebe ("Dokumentace, plný běh testů a finální UX review"). Tree: branch
`redesign2`, on top of `50aa7fbc` (phase 7d), uncommitted working tree of this phase.

Everything in this directory is a RUN ARTEFACT. The product changes of this phase are listed at the
bottom (`## What phase 8 changed`).

## 1. Unit tests (per project, sequential — never the whole solution in parallel)

| Project | Result |
|---------|--------|
| `tests/Tempo.Blazor.Tests` (bUnit, full) | 14258 passed / 5 failed / 1 skipped (14264) — 3 pre-existing scheduler culture reds + 2 contention flakes (both green in isolation, see §1.1) |
| others | see `logs/other-units.log` |

### 1.1 Pre-existing bUnit reds (NOT regressions of this plan)

| Test | Class | Verdict |
|------|-------|---------|
| `TimeGrid_Renders_All_24_Hours_By_Default` | `TmSchedulerTimeGridPhase5Tests` | PRE-EXISTING: the spec asserts a 24-hour `"0:00"` label without pinning a culture, so it follows the process culture. Reproduced on a clean `HEAD` worktree (`git worktree add /tmp/wt-head HEAD`, 3/3 failed identically) — Scheduler is untouched by this plan. |
| `TimeGrid_Renders_Hours_From_WorkDayStart_When_ShowFullDay_False` | `TmSchedulerTimeGridPhase5Tests` | same (A/B worktree, same failure) |
| `Title_Is_A_GoToDate_Button` | `TmSchedulerToolbarTests` | same (asserts `červen 2025` while the label formats as `June 2025` under the lane's `en_US.UTF-8`) |
| `QueryInput_Dismissal_CancelsPendingDebounce_DropdownStaysClosed` | `TmQueryInputTests` | CONTENTION FLAKE: red only inside the full parallel run, green when re-run alone (`Total tests: 2, Passed: 2`) |
| `Upload_ReportsProgress_EndingAt100` | `TmChunkedUploaderTests` | CONTENTION FLAKE: red only inside the full parallel run, green when re-run alone |

Reported to the plan (`plan_report_remaining`) instead of being "fixed" here: they are outside the
Spreadsheet / Notion / Modal / Wireframe / core-modal areas this plan touched.

### 1.2 Node lanes (`npm run test:*`)

| Lane | Result |
|------|--------|
| `test:spreadsheet` | 55 passed / 0 failed |
| `test:overlay` | 637 / 0 |
| `test:wireframe` | 126 / 0 |
| `test:diagram-editor` | 122 / 0 |
| `test:email-templates` | 20 / 0 |
| `test:pdf-viewer` | 112 / 0 |
| `test:reporting-modules` | 4 / 0 |
| `test:document-editor` | 627 / 0 |
| `test:document-editor-modules` | 627 / 0 |
| `check:document-editor-bundle` | "headless layout bundle is up to date." |

### 1.3 Static gates

- `JsonDocumentation generate` + `validate --fail-on-drift` → exit 0, 27 packages, 3317 items, no drift.
- `node scripts/audit-css-strict.mjs` → 4451 violations, baseline 4451, **no new violations**.
- `node scripts/audit-css-tokens.mjs` → 388 stylesheets / 623 `--tm-*` definitions, no undefined token.
- `ScopedCssOwnershipTests`, `SpreadsheetDesignTokenTests`, `DataTestIdConventionGuardTests`,
  `SourceFileLengthTests`, `DemoResourceCompletenessTests` → 21/21 green (two of them were RED before
  this phase and are part of its fix set — see `## What phase 8 changed`).

## 2. E2E suite — full lane attempted, plan-area lane complete

The lane is the local port of `scripts/run-e2e-full.ps1` (`run-e2e-shards.sh` + `e2e-shards.mjs` in this
folder) because `pwsh` is not available on this box: the suite is split BY TEST CLASS into 8 sequential
shards (the demo hosts bind fixed ports, so shards must not overlap), `TestCategory!=Performance` is
excluded, orphaned hosts are reaped by port between shards, and each shard writes its own TRX.

### 2.1 What actually ran

| Leg | Result |
|-----|--------|
| **Full lane (8 class shards, `shard-plan.tsv`)** | **NOT COMPLETED.** Shard 1 (78 classes, incl. the whole `SpreadsheetE2ETests` partial class, the Notion blocks/menus, the Wireframe and the overlay classes) ran **4 h 40 m** and was stopped before its TRX was written — the runner writes one TRX per shard at the END, so shard 1 produced no TRX, only `logs/shard-1.log` with every failure and stack trace. The measured throughput on this box is **~60–90 s per test** (concurrent load from other sessions: load average 5–11 on 12 cores), i.e. ~20–25 h for the ~1500-test suite; that is not finishable in one session. The remaining shards were therefore not started. |
| **Shard-1 residual (16 failures re-run ALONE)** | `residual/shard-1-failures.trx` + `residual/summary.md`: **12 passed / 4 failed** — so 12 of the 16 lane reds were contention flakes (including all three that hit this plan's areas: `Phase7c_LiveThemeFlip_RepaintsTheCanvasWithoutResize`, `CanvasRenderer_WheelScrollStaysOnLogicalHotPath`, `Phase3_Dialogs_Tablet1024Touch_StayDialogs_WithTouchSizedTargets`… the last one is NOT a flake, see below). |
| **Plan-area lane (this phase's own fixes)** | `plan-area/phase8-fixes.trx` (+ `plan-area/review-fixes.trx` after the review round): **4/4 passed** — `Phase8_DensitySwitch_RepaintsTheSameInstance_AndLeavesTheModelAlone` (new), `Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView` (extended with the grid-floor assertions of finding 1), and both `WireframeSpreadsheetStencilE2ETests` methods (the new stencil captures). |
| **Phone (390/320) regression re-run after the `overflow-y` change** | `plan-area/phone-overflow-rerun.trx`: the nine Spreadsheet phone specs that touch the scrolling phone root — `Phase6_Mobile_390_CompactChrome_WithActionBar`, `Phase6_Mobile_390_ContextMenu_IsASheet`, `Phase6_Mobile_BasicCommands_ReachableInTwoTaps`, `Phase6_Mobile_ContextSheet_SurvivesGridScroll_AndClosesOnEscape`, `Phase6_Mobile_FilterSheet_Footer_IsAboveTheActionBar`, `Phase6_Narrow_320_HasNoHorizontalOverflow`, `Phase7_Mobile_TapSelects_LongPressOpensTheSheet`, `Phase7_Mobile_DoubleTapEdits_NumericCellUsesTheDecimalKeypad`, `FindReplaceDialog_Mobile390_OpensAboveTheDrawer` — **9/9 green**. The batch's single red (`Phase6_Mobile_390_CompactChrome_WithActionBar`, 4 m 11 s) was a cold-boot/contention timeout and passed on the isolated re-run (3 m 15 s). |

Four reds are NOT flakes — each reproduced on a clean worktree (`git worktree add /tmp/wt-ab <commit>`,
same spec, same failure). The first is a regression of the PLAN (phase 6), the other three are
pre-existing and outside the plan's areas:

1. `Phase3_Dialogs_Tablet1024Touch_StayDialogs_WithTouchSizedTargets` — the tablet "More" entry of
   *Custom sort…* is found and is "visible, enabled and stable", but its box is **outside the viewport**
   (`element is outside of the viewport`, repeated for 30 s), so Playwright never clicks it. The core
   overflow menu's placement at the two-row tablet chrome is the suspect. It also fails with the phase-8
   CSS reverted, and it fails identically at `50aa7fbc`.
2. `Selector_Desktop1440_SidebarFilter_AddedState_AndKeyboardOnly` + `Selector_Mobile390_Sheet_ChipFilter`
   (`DashboardF6SelectorE2ETests`) — fail alone too; Dashboard is untouched by this plan.
3. `DatePicker_At390_InModalSheet_OpensNestedSheet` — fails alone too; pickers are untouched by this plan.

All four are reported to the plan via `plan_report_remaining` instead of being fixed here (three are
outside the Spreadsheet/Notion/Modal/Wireframe areas this plan touched; the first is in the plan's area
but its root cause is the CORE overflow-menu placement, which is outside this phase's review budget).

**Verdict for `Phase3_Dialogs_Tablet1024Touch_StayDialogs_WithTouchSizedTargets` (bisect, review round).**
The spec's file first appears at `9edb143b` (phase 3), so a pre-plan worktree cannot run it at all
(`8a9bc9bd` has no such file). The worktree recipe was therefore applied to the plan's own history:

| Tree | Result |
|---|---|
| `b1a8fc10` (phase 5 — BEFORE the container-driven chrome) | **PASSED** (6 m 19 s) |
| `32305292` (phase 7c — phase 6 + 7 + 7b + 7c) | **FAILED**, identical 30 s timeout, "element is outside of the viewport" |
| `50aa7fbc` (phase 7d) | **FAILED**, identical |
| `redesign2` working tree of this phase | **FAILED**, identical — and it also fails with the phase-8 `overflow-y` rule reverted |

Verdict: **the red was introduced by the phase-6 container-driven chrome commit (`e0febc25`)**, i.e. it is
an in-plan regression, not environment noise and not a phase-8 change. The mechanism is visible in the
bisect: before phase 6 the 1024 px viewport resolved the DESKTOP chrome, so *Custom sort…* sat directly
on the ribbon and was clickable; from phase 6 the container (~700 px) resolves the TABLET chrome, the
command moves into the row's "More" menu, and that menu is a core `ActionOverflowMenu` panel anchored
BELOW its trigger — with the trigger near the bottom of the demo viewport the entries are painted outside
the viewport and nothing scrolls a top-layer panel into view (Playwright reports "element is visible,
enabled and stable … element is outside of the viewport" for 30 s). A user hits the same thing until the
page is scrolled, so this is a real (if narrow) tablet UX defect, not only a spec artefact.

It was NOT fixed here: the placement lives in the core `ActionOverflowMenu` (shared with the diagram,
wireframe and dashboard toolbars), the phase-6 commit `e0febc25` is outside this phase's review budget,
and 7d's own lane evidence recorded the `Phase3_` group green — so the regression slipped between that
lane run and now. Two candidate fixes are recorded on remaining task f25a9d47: flip the overflow panel up
when the trigger is below the fold (core), or move that spec to the documented
open-on-desktop-then-resize pattern (test-side).

#### 2.1.1 FIXED (f25a9d47) — the overflow panel clamps and scrolls instead of leaving the viewport

Root cause, measured (not inferred): the panel is in the browser TOP LAYER, so no page scroll can bring
its tail back, and `overlay.js` only flipped + clamped — it never capped the height. A panel taller than
the viewport therefore landed at the top margin and ran past the bottom edge. Real numbers captured by
the new `SpreadsheetPhase8OverflowE2ETests` BEFORE the fix:

| Case | Panel box before the fix |
|---|---|
| tablet 1024×768, primary row "More" | `y 8 .. 1217` (height 1209) of a 768 px viewport — 449 px off-screen |
| tablet 1024×430 (short window, trigger near the bottom), formatting row "More" | `y 8 .. 2801` (height 2793) of a 430 px viewport — 2371 px off-screen |

Fix (core, one additive knob the panel already had): `ActionOverflowMenu` now opens its `TmOverlayPanel`
with `ConstrainHeight`, so the shared placement caps the panel to the room on the resolved side and
scrolls it internally (`overflow-y: auto`), while `Flip` + `Shift` keep deciding the side. No public API
change (`ConstrainHeight` already exists on `TmOverlayPanel`), no change for a menu that fits below its
trigger, no JS change. Files: `src/Tempo.Blazor/Components/Actions/ActionOverflowMenu.cs`,
`docs/overlays.md`, `docs/toolbar.md`, `CHANGELOG.md` (Fixed, core).

Tests (first run of each is the recorded RED — the RED E2E numbers are the table above):

| Lane | Before | After |
|---|---|---|
| `TmToolbarOverflowTests.MoreMenu_AsksThePanelToStayInsideTheViewport` (new, bUnit) | RED — the open options carried `"constrainHeight":false` | green; full `Toolbar\|Overlay\|ActionOverflow\|Spreadsheet` lane 2028/2029 (the single red is the pre-existing `TmSchedulerToolbarTests.Title_Is_A_GoToDate_Button` culture red of §1.1) |
| `SpreadsheetPhase8OverflowE2ETests` (new, 2 specs) | RED 0/2, the boxes above | **2/2** (1 m 04 s) — panel inside the viewport, content taller than the panel, panel scrolls, LAST entry reachable, and "Custom sort…" opens the Sort dialog from a 430 px viewport |
| `Phase3_Dialogs_Tablet1024Touch_StayDialogs_WithTouchSizedTargets` (the bisected red) | RED (30 s, "element is outside of the viewport") | **1/1 PASSED** (5 m 27 s) with no test-side change |
| Core toolbar-overflow consumers | — | 4/4: `Phase6_Tablet_1024_TwoCompactRows_NoTabStrip`, `Phase6_Mobile_BasicCommands_ReachableInTwoTaps`, `DocumentEditorToolbarOverflowAndRenderersE2ETests.OverflowController_NarrowViewport_ShowsMoreMenu_WideViewport_HidesIt`, `MobileActionBarE2ETests.Demo_390_Bar_Sticky_MoreSheet_ConfirmAboveSheet` |
| `npm run test:overlay` (node, the placement math) | — | 637/637 |
| `ComponentDocumentationFreshnessTests` + `MarkupClassCoverageTests` + all `TmToolbarOverflowTests` | — | 56/56 |

Screenshots: `tests/Tempo.Blazor.E2E/__screenshots__/spreadsheet/phase8-more-panel--tablet-1024.png` and
`phase8-more-panel--tablet-1024-short.png` (the panel capped inside the viewport; the short-window shot
shows the Sort dialog it opened). Re-running the tablet dialog spec regenerated seven tracked
`phase3-*-tablet.png` baselines (they were stale from the run that died mid-spec) — no assertion or
product change, committed with the phase under decision `SPREADSHEET-BASELINE-POLICY`.

### 2.2 How to finish the lane

`bash run-e2e-shards.sh <dir> 8` re-runs the whole lane with the same class split; `bash
run-e2e-shards.sh <dir> 8 3,5` re-runs only shards 3 and 5. Both write `shard-<n>.trx` plus
`summary.md`/`summary.json` through `e2e-summary.mjs`. Run it on an idle box (or overnight) — the
throughput, not the suite, is what broke this attempt.

## 3. Final UX review

Reviewed against `output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/*.png` (57 references) and
the gap analysis `output/imagegen/tempo-reimagined/analysis/spreadsheet.md`; the binding decisions
(G2 / G6 / SP1–SP4) and the deliberate deviations documented in `docs/spreadsheet.md` were treated as
the acceptance baseline, not as findings.

### Findings

| # | Severity | Where | Finding | In scope | Verdict |
|---|----------|-------|---------|----------|---------|
| 1 | major | `__screenshots__/spreadsheet/phase6-notion-edit-modal-568x320.png` | The Notion fullscreen editor at 568×320 showed the formula bar, the column headers, the sheet tabs, the action bar and the footer — and **no grid rows at all**. The fixed chrome (toolbar 60 + formula bar 36 + column headers 28 + sheet tabs 30 + action bar 56 + footer 64) is taller than the 320px host, and because the modal deliberately sets `--tm-spreadsheet-mobile-min-height: 0` (so the Save / Discard footer can never be pushed out of the viewport) the grid flex item shrank to ~2px. The sheet was editable in theory and invisible in practice. | in | **FIXED test-first**: `--tm-spreadsheet-grid-min-height` (9rem) floor on the phone grid + `overflow-y: auto` on `.tm-spreadsheet--layout-mobile`, so the phone root scrolls when the host cannot fit chrome + floor (and never scrolls otherwise). Guarded by `SpreadsheetDesignTokenTests.MobileLayout_KeepsAUsableGridFloor_WhenTheHostIsShorterThanTheChrome` (RED before the rule, GREEN after) and by `SpreadsheetPhase6ResponsiveE2ETests.Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView` (grid ≥ 96px and reachable by scrolling, at 844 and 320). |
| 2 | nit (unverified) | `__screenshots__/spreadsheet/phase6-mobile-320-chrome.png` | At a 320px container the phone command row looks ~15px taller than at 390 (an extra light band before the formula bar). The DOM says one core `TmToolbar` row (`Overflow=Menu`, `min-height: 3rem`), so this is most likely the toolbar's own 44px touch target + padding rather than a second row; it could not be re-measured on device during this phase because the E2E lane held the demo hosts. | in (Spreadsheet chrome) | **NOT A DEFECT as far as the markup shows** — recorded as a device-verification note instead of a code change (`plan_report_remaining`). Nothing was changed for it: shrinking the touch target would violate the 44px floor. |
| 3 | nit | all `phase2-desktop-1440--*.png` | The ribbon is wider than the 1078px demo card, so the last group (`Cells`) is cut mid-glyph (`D`) and only reachable by scrolling the ribbon. | documented deviation (the ribbon scrolls horizontally by design; the reference is a 1536px-wide mockup) | accepted, no change |
| 4 | nit | `phase6-mobile-390-main.png`, `phase6-mobile-320-main.png` | The phone chrome renders no compact header row in the demo because the demo instance has no `Title`/host slots; the reference has a document title. | documented (an empty 50px header row is worse than none; the header appears as soon as a host gives it content) | accepted, no change (asserted by `Phase6_Mobile_390_CompactChrome_WithActionBar`) |
| 5 | nit | the phase 7 touch captures (`phase7-mobile-390-touch--selection.png`, `phase7-tablet-1024-touch--*.png`, `phase6-mobile-*`) | The bottom-right touch "end" drop of the selection stays visible at 16 px and sits over the FIRST characters of the neighbouring cell's text, so a short value next to a selected cell reads as if it were clipped. | in (Spreadsheet chain canvas) | **BACKLOG** (documented deviation): the corner drops are drag-only, `aria-hidden` and clipped to the grid body by `clipTouchHandleRect`; shrinking them below 16 px would break the 24/44 px touch floor that the same round of gesture tests pins. Recorded as remaining work, not silently accepted. |

**Verification of finding 1 (post-fix):** `plan-area/phase8-fixes.trx` — the extended
`Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView` passes at 844 AND 320 px of viewport
height, i.e. the grid measures ≥ 96 px and is reachable by scrolling the phone root, while the Save /
Discard footer stays inside the viewport.

The committed `phase6-notion-edit-modal-568x320.png` was **STALE** at the time of the table above (it was
the pre-fix baseline, and the spec only rewrites it when it re-runs). It is now refreshed: the spec was
re-run (`plan-area/review-fixes.trx`, green) and the new PNG shows **four grid rows** above the sheet
tabs, the action bar and the footer.

### What was verified as matching the references

- **Desktop 1440, light / dark / indigo** (`phase2-desktop-1440--{light,dark,indigo}.png`): the token
  chain reaches the ribbon, the formula bar, the grid, the sheet tabs and the status bar; the canvas
  paints its gridline, selection ring, active row/column header strip from the same palette (the dark
  active header is opaque — `phase7b-dark-active-header--{desktop,indigo}.png`).
- **Tablet 1024 touch** (`phase6-tablet-1024-{main,chrome}.png`, `phase7b-tablet-1024-comfortable-gutters.png`):
  two compact rows, no tab strip, Comfortable gutters (56/28), 44px targets, status bar present.
- **Phone 390/320** (`phase6-mobile-{390,320}-{main,chrome}.png`): one command row, formula bar, grid,
  sheet tabs, the core mobile action bar with Insert / Format / Image / More, no status bar.
- **Dialogs**: every desktop/tablet/mobile trio (`phase3-*`, `phase4-*`) is on the same core
  `TmModal` frame — same header, same X, same footer order; the phone presentation is the bottom sheet
  or the fullscreen panel; `phase4-*` supersedes `phase3-*` where both exist (phase-4 content rework).
- **320 px dialog pass** (`phase7c-name-manager--mobile-320*.png`, `phase7c-sort--mobile-320.png`,
  `phase7c-format-cells--mobile-320.png`): fullscreen panel, one-column card list, wrapped "Refers to"
  with the full formula in `title`, no horizontal scroll, footer reachable.
- **Wireframe stencil** (new, `__screenshots__/wireframe/spreadsheet-stencil/`): desktop / tablet /
  mobile variants of the `TmSpreadsheet` stencil, captured by the new
  `WireframeSpreadsheetStencilE2ETests` — the manual remaining item carried out of phase 7d.

### Baseline churn (investigated, decision recorded)

The spreadsheet captures under `__screenshots__/spreadsheet/` are a mix of PAGE shots
(`phase2-desktop-1440--*`, `phase6-*--main`, `phase6-*--chrome` is element-scoped,
`phase7c-modal-header--*`, `phase7b-dark-active-header--*`) and ELEMENT shots (`phase3-*`/`phase4-*`
dialog shots are element-scoped). The page shots churn whenever an unrelated demo section above the
capture changes height. That is a known, accepted cost: the page shots are the artefacts that show the
app-level context the UX review compares against the mockups, and the dialog evidence is already
element-scoped. No new baseline-churn policy was introduced in this phase; the policy decision of phase
7d (refresh baselines only in their own commit, never by copy) still holds.

## What phase 8 changed

| File | Change |
|------|--------|
| `src/Tempo.Blazor.Spreadsheet/wwwroot/css/components/_spreadsheet.css` | `--tm-spreadsheet-grid-min-height` (9rem) + the phone grid floor + `overflow-y: auto` on `.tm-spreadsheet--layout-mobile` (finding 1) |
| `src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/TmSpreadsheet.razor` | the four hardcoded `data-testid` literals now go through the `@TestId(...)` convention; `@inherits TmComponentBase` (fixes the RED `DataTestIdConventionGuardTests`) |
| `src/Tempo.Blazor.Wireframe/.../TempoNativeRendererDefinitions.Spreadsheet.cs` (new) | the ~460-line `TmSpreadsheet` stencil block moved out of `TempoNativeRendererDefinitions.cs` (1479 → 1012 lines, under the 1200-line ceiling — fixes the RED `SourceFileLengthTests`); the class became `static partial`, the markup is byte-identical |
| `tests/Tempo.Blazor.E2E/WireframeSpreadsheetStencilE2ETests.cs` (new) | stencil captures desktop / tablet / mobile + the `auto` = desktop (X11) assertion |
| `tests/Tempo.Blazor.E2E/SpreadsheetPhase6ResponsiveE2ETests.cs` | the 568×320 modal asserts a usable, reachable grid |
| `tests/Tempo.Blazor.Tests/.../SpreadsheetDesignTokenTests.cs` | the CSS guard for the phone grid floor |
| `tests/Tempo.Blazor.E2E/SpreadsheetPhase2VisualE2ETests.cs` | new `Phase8_DensitySwitch_RepaintsTheSameInstance_AndLeavesTheModelAlone` — the demo's live density switch repaints ONE instance both ways (Comfortable → Compact → Comfortable) and the shot `__screenshots__/spreadsheet/phase8-density-switch--desktop.png` |
| `README.md` | the consumer note: split editors that need the core pointer helpers must serve `_content/Tempo.Blazor/js/`, and the spreadsheet package theme layer is required when a host imports the individual stylesheets |
| `docs/spreadsheet.md`, `COMPONENTS.md`, `CHANGELOG.md`, `src/Tempo.Blazor.Demo.SharedUI/Pages/SpreadsheetPage.razor` (+ `DemoResources*.json`) | documentation set: host-short-host contract, `TmSpreadsheetCanvasGrid.SuppressFocusRestore`, the two new components and the new parameters, the plan summary with the pending perf escalation, and a live `Density` switch in the demo |
