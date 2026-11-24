# Waiver — publishing v2.13.0 without a second full E2E run over the released commit (DEC-TEMPO-RELEASE-GATE, second clause)

**Decision:** owner `ptyll`, 2026-11-24 (plan 1c95309c). `v2.13.0` is published WITHOUT a second owner-verified green full-suite E2E run over the released commit. Scope: ONLY the tag `v2.13.0`.

## What was measured, and on what

| | |
|---|---|
| Full run 1 | `3da8245a` (`TestResults/e2e-full-213-r1`): 3952 total, 3750 passed, 20 failures in 14 tests, 182 skipped. Failures listed in `TestResults/e2e-213-failures.tsv`. |
| Targeted rerun | All 14 failing tests were root-caused, reviewed APPROVED and green through `--filter` (`TM_E2E_REQUIRE_SMTP4DEV=1`). No second full lane (owner decision 2026-11-24). |
| Unit / node | bUnit and node lanes of every touched package green per the reviewed fix commits. |

## The 14 tests, commits and targeted rerun

Spreadsheet (commit `38cb1a2cfd525d6bb7344521569fe92028924ec5`; reviewer green: 7/7 in 7 m 23 s):

1. `SpreadsheetE2ETests.CanvasJsEngine_InlineFormulaEditorShowsSharedFunctionHint`
2. `SpreadsheetE2ETests.CanvasRenderer_JsFormulaReferenceHighlightsRemainVisibleAfterScroll`
3. `SpreadsheetE2ETests.CanvasRenderer_DoubleClickStartsCellEdit`
4. `SpreadsheetE2ETests.CanvasRenderer_UnchangedLocalEditDoesNotRedrawContent`
5. `SpreadsheetE2ETests.CanvasJsEngine_FormulaSessionTransfersFromInlineEditorToFormulaBarWithoutLosingCaret`
6. `SpreadsheetE2ETests.CanvasJsEngine_InlineFormulaEditorAutocompleteAcceptsFunctionFromKeyboard`
7. `SpreadsheetE2ETests.Phase7_Tablet_TapSelectsCell_AndDoubleTapStartsEditing`

Remaining seven (commit `560bb15157077259baf72c24bc004146a746b526`; reviewer green: 13/13 methods in 8 m 50 s, then 87/87 across the 7 classes in 21 m 47 s):

8. `PdfViewerContinuousE2ETests.PdfViewer_Continuous_ScrollUpdatesPageIndicator`
9. `NotionMentionMenuE2ETests.EB5_PageLinkMenuNoResultsAndLongTitleChip_CaptureBaseline`
10. `NotionDatabasePhase8E2ETests.Table_StaysReadable_AndFirstColumnSticks`
11. `TmTimeSelectorE2ETests.TimeSelector_EnableSwipe_PointerDragUp_StepsHours`
12. `NotionDatabasePhase9E2ETests.Surfaces_Screenshots`
13. `DocumentEditorCanvasContentControlsE2ETests.PhaseE9_AdvancedControlsNavigateRepeatSaveReloadAndScreenshot`
14. `Feedback.OverlayInitialFocusE2ETests.InitialFocus_SurvivesASlowPromote_WhenTheRootHasNoOwnDisplay`

Review findings around those fixes: `43c48833c63b2535393f020965c76785c3418001`.

## What was NOT measured

No second full-suite run covers the released commit. Regressions outside the 14-test set, introduced after `3da8245a`, are covered only by the targeted rerun and the per-fix node/bUnit lanes. The testhost crash when running the whole `SpreadsheetE2ETests` class (around `FormulaBarMixedFormulaDragRange…`) is out of scope for this waiver.

## Scope

- ONLY the tag `v2.13.0`: both publish workflows skip the "Verify the release evidence" step iff `github.ref == 'refs/tags/v2.13.0'` AND this file exists.
- The evidence gate stays fully active for every other version, tag, branch and workflow_dispatch; the v2.11.0, v2.11.1 and v2.12.0 waivers are unchanged.
- Removing this file re-arms the gate for v2.13.0. The next release needs a fresh recorded full run.
