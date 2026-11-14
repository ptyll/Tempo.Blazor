# FIXES — e2e-full-fze6 failures (measured on d4ddfe7) → fixed in 34dbd601

Fix commit: `34dbd601` "fix(release-gate): E2E failures — modeling inspector draft gate, pdf thumbnails param, diagram right-click
hit-test, editor shell containment, dock floor; tests updated to 2.11.0 contracts, locale pinned" (81 files; `src/`: 6 files, rest tests/CHANGELOG).
Test names below are from `summary.json` (63 distinct fully-qualified names, 65 failures — a few names fail in several shards/data rows).

**Verification column** — targeted rerun on 34dbd601 (owner-reported): 147/148 green, the last one 2× green. The
per-test rerun log is NOT in this bundle; rows marked "no change" were not touched by 34dbd601 (see notes).
Serial on d4ddfe7: R = still red, G = green serially (rerun2/rerun3), – = not rerun serially on d4ddfe7.

## Product bugs (src/)

| Failing test(s) | Root cause | Fix (34dbd601) | d4ddfe7 serial |
|---|---|---|---|
| ModelingAccessibilityM25 `Editing_InputsLabeledAndValidationErrorAnnounced` | `TmModelingInspector` re-seed gate compared the incoming committed value with the *draft*; the editor root re-renders on Delete in a text field, resetting the field and losing the pending "name required" message | `TmModelingInspector.razor.cs`: the gate compares with the values the drafts were seeded from (+ bUnit test `TmModelingInspectorEditingTests`) | – |
| `PdfViewer_Tablet_TouchTargets44` | `TmPdfViewer` compared `ThumbnailsCollapsed` with viewer *state* (carries the tablet layout default) — any re-render re-delivering `false` re-opened the panel | `TmPdfViewer.razor.cs`: parameter compared with its previous value (+ bUnit test `TmPdfViewerTests.Responsive`) | R |
| ModelingOpenInEditorDialog `Escape_*` (4), ModelingBpmnProfileM17 / ModelingUmlProfileM18 `DialogPalette_*`, `Dialog_toolbox_pins_notation_palette_and_inserts_from_it` (right-click / context-menu paths) | Right-click on a node centre hit a connection point and started an edge drag instead of the node context menu; right-click on the just-selected edge's handles resolved to the empty canvas (tests also needed the dialog's editor JS attached and `editing=1`, see below) | `diagram-editor.js`: right-clicks no longer start an edge draw; a handle belongs to its edge (+ node test `diagram-editor-engine.test.mjs`) | R (Dark… rows) / – |
| `ModelingTabletTouch…Tablet_1024_OpenInEditorDialog_MinimapStartsCollapsedAndReopens` and diagram-in-dialog body timeouts (10 s) | `TmDiagramEditor` root and its `TmEditorShell` were *both* inline-size containers around the same box (`tm-diagram-editor` + `tm-layout`); Chromium intermittently skipped layout of shell header/main/status | `_diagram-editor.css`: the shell resets its own containment inside the editor (+ `DiagramEditorCssTokenTests`) | – |
| ModelingPhase5Review `Editor_1440_IsDesktopWithUsableCanvasAndDock`, `Editor_1440_MixedScenarioDockFitsTwoRows` | Issues-dock floor 14rem predates the phase-11 filter bar (≈41 px) — room for < 2 issue rows | `TmModelingEditor.razor.css`: floor 15rem | – |

## Tests updated to the 2.11.0 contracts (tests/)

| Failing test(s) | Root cause | Fix (34dbd601) | d4ddfe7 serial |
|---|---|---|---|
| `…ScopedAxeScan_HasNoCriticalOrSeriousViolations` (Analytics/Sortable/Segmented/TraceWaterfall/UsageMeter, Server/IA/WASM), `Axe_Scoped_To_TheNewSections…`, `Chart_IsNamedByItsHeading…` | axe sampled resolved colours while a finite CSS animation/transition (fade-in, colour transition, segmented indicator) still ran → blended low-contrast frame | axe helpers wait for finite `document.getAnimations()` + 2 rAF (34 files; looping ones, e.g. skeleton shimmer, not awaited) | R (Axe_Scoped, Chart_, ScopedAxe) / – |
| `TheSlidingStripIsMeasuredAndClaimed` | same animation-settle race (segmented indicator) | animation wait in `SegmentedControlE2ETests` | R |
| PointerDrag `Scheduler_Touch_*` (5), SchedulerTouch `*` (7: Week/Day/Timeline) | browser context without locale inherited cs-CZ of the dev box: labels read `Týden`/`Den`/`Časová osa` while specs expect `Week`/`Day`/`Timeline` | `Locale = "en-US"` pinned in 23 contexts (PlaywrightTestBase + per-class contexts) | – (named rows `po.*`, not rerun) |
| `Scheduler_ViewsWork` (InteractiveAuto) | pill tablist is drawn only while the toolbar container is ≥ 60rem; the demo column is narrower → switching via the compact view dropdown | uses the shared view-switch helper (both shapes) | R |
| `Mobile_Card_Action_Bar_Opens_View_And_More` | `scheduler-view-menu` became the stable id of the view switcher's layout wrapper (hidden); the open dropdown panel is a sheet/popover outside it | spec waits for the menu's `[data-view=Week]` options instead of the wrapper (`SchedulerResponsiveE2ETests`) | – |
| `Demo_Dark_And_Indigo_PaintTheShellSurfaces`, `Tablet_Overlay_Dark_RidesTheTokens`, `Dark_MoreMenuAndButtons_UseThemeSurfaces`, `CanvasBackdrop_FollowsTheTheme`, `DiagramScreenshots_CaptureLightAndDarkThemeBaselines` | the demo frame owns the theme (its own `data-theme` island re-pins light tokens); setting `<html data-theme>` never darkened the content. Diagram canvas is now themed (TG-004): contract is 4.5:1 of node text vs the resolved `--tm-canvas-paper` | new `EnsureDemoThemeAsync` (real header toggle) in `PlaywrightTestBase`; canvas contrast asserted against the resolved paper token | R (Dark_More, Demo_Dark, DiagramScreenshots, CanvasBackdrop ×2) |
| `EveryBaselineGeneratorInheritsTheOptInGate` | evidence-screenshot generator naming | `NotionPhase1BaselineScreenshots` → `NotionPhase1EvidenceScreenshots` | R |
| `SheetRoots_ResetUaPopoverDefaults` | since the popover box fix (NotionEditor phase 5) the promoted root is pinned to the viewport: computed max-height is 100dvh, not `none` | accept `none` or the viewport height (never smaller) | R |
| `NotificationBell_At390_Touch_OpensAsTitledSheet…` | contract changed: the in-panel title is no longer dropped in sheet mode | spec expects `.tm-notification-bell__title` next to the sheet header title | R |
| `QrCode_Is_A_Named_Graphic_Without_Inline_Style`, `QrCode_Download_Link_Carries_A_Standalone_Svg` | locale (cs-CZ names) | QR locale pinned en-US in `TmQRCodeE2ETests` | R |
| `Editing_ManualSave_AggregatesAndClears` | persisted-edit reload only works on the `scenario=layout-persist` route (default route always serves the shipped model) | spec uses `layout-persist`, `editing=1` | R |
| `MediaLibrary_SelectItem_SetsImageOnBlock` | the library items are external picsum.photos images with lazy `<img>`: "visible" depended on network (times out offline) | asserts the `<img>` is attached with the item's `https://` src | – |
| `InitialFocus_LandsInside_OnRepeatedOpens` | mobile Gantt container is read-mostly (G2): no export/import dialog to open | the mobile data row opens the filter dialog the mobile chrome has | – |
| `FacetSheet_ScrollsItsBodyUnderAPinnedFooter…` | measured while the slide-in was still running (footer 595 vs 542 px) | settle wait before measuring | R |
| `Chat_RenderWindow_Server_TailOnly_DeepLink_Trim` | away-append scenario needs the reader sitting on the anchor; over a circuit focus lands before the restore scroll finished | wait until the viewport rests on the anchor row (`ChatScrollE2ETests`) | R |
| `CircuitReconnect_RestoredPopoverRoots_Stay…` | Server demo defines its own reconnect bar (state classes) — default-shadow-root probe timed out | reconnect probe handles every UI shape (`OverlayTopLayerRenderModesE2ETests`) | – |
| `Perf_TwoHundredElements_FirstRender_AndRerender` | wall-clock perf budget cannot hold in a parallel 8-shard run | moved to the Performance lane; `Perf_ThirtyElements` added to the regular lane | R |

## Failures with no dedicated change in 34dbd601

`SpreadsheetE2ETests.Phase7_Tablet_TapSelectsCell…` (G), `…CanvasRenderer_JsFormulaReferenceHighlights…` (R), `NotionCodeBlock…PreviewToggleHidden…` (G),
`PdfAnnotator_Continuous_HighlightOnPage2` (G), `ModelingLayoutPhase4…MovingATaskBetweenLanes…` (G), `ReadinessInfrastructureTests.NoSpecKeepsALocalColdBootRecoveryCopy` (R).
Four passed serially on d4ddfe7 (parallel-load flakes, same class as the 2026-09-27 run); the other two are covered only by the owner's
targeted rerun on 34dbd601 (this bundle holds no per-test log for it).

