# Inventory — unresolved remaining tasks + carry-forwards of plan faf503eb (Redesign: Základ), main @ 846bc242

Source: plan_get_completion_report(faf503eb-bf08-4f5f-8c33-08df2362aa32). App 3bc0cab8-925b-459e-840b-6fee830dfcee.
Each item: [ID] short — full text is in the completion report (call it to read the full description).

## Carry-forwards
- [eddd747f] DE-1.2: TmDocumentEditorToolbar onto core overflow (ToolbarButtonPriority/tm-toolbar.js/ActionOverflowMenu). Belongs to DocumentEditor plan 146c7e1b?
- [d2eb082e] TmDiagramEditor ~32 buttons onto TmToolbar Overflow=Menu + Labels=Below. Belongs to DiagramEditor plan 94d441f2?

## Remaining tasks
- [9e46d2da] + [17b6d82d] Pre-existing env unit failures (FullCloneWorkflow x3, ReleaseGateFilter x4/x5, PublishEnvironmentGate x1, ReleaseEvidence/ReleaseContract, TmTabsHeaderSlots frozen baseline).
- [8b3c2021] Migrate ~25 component rules --tm-shadow-focus → --tm-focus-ring; _calendar-view.css dark blocks + hex fallbacks into tokens; NotionEditor _notion-variables.css nested-dark aliases + rgba literals.
- [421a41ce] Dark --tm-cal-range-bg contrast (~1.1:1) + contrast test.
- [02dd6146] JsonDocumentationGenerator ExtractMembers misses expression-bodied props + positional record params.
- [26e1afd5] F2 follow-ups (1) TmDialog prompt IME IsComposing guard (2) OverlayRound6E2ETests summary (3) ScrollRegionInterop detach race/children observe/title resync/NRE catch (4) Custom TmDialog scroll-region initial focus race (5) CHANGELOG TmButton clause (6) docs focus-scope-migration + bottom-sheet flick doc + 3-snap node test (7) stale comments (8) shared lazy JS module helper.
- [e5f236e8] ContextMenu/SplitButton/NotificationBell mobile presentation stays Popover — sheet design if ever.
- [07411bd1] positionFloating/place option vocabulary aliases (offset/shift vs anchorGap/clamp).
- [efdc933b] Keep CSS bundle regenerated with every CSS-touching commit (guard?).
- [0e390ce4] Extension-package z-index dialogs (RTE, Notion*, Spreadsheet, DocumentEditor, Signing, dashboard widget selector) not promoted to top layer.
- [3f1747c0] Verify top-layer promotion on Blazor Server + InteractiveAuto demos (dev cert 7107/7108).
- [c51ddf81] F3 follow-ups (1) activate-after-promote focus race (Lightbox, CommandPalette, KeyboardShortcutsHelp, maybe Modal/Dialog) shared helper + E2E (2) TmGanttImportDialog IsOpen guard after await (3) ComponentChangeCarriesJsonTests EOL commit 975a75be→98cc1c99 (4) focus after dismissing a toast under a modal trap (5) palette backdrop click closes; Lightbox Escape focus restore (6) roving in TmDropdown [DONE in F4?] (7) Context/Split/Bell mobile (8) PanelPresentation rename [resolved] + positionFloating vocab (9) gallery images [DONE] (10) package dialogs (11) Server/IA verification (12) DataTable export rows vs mockup.
- [7800d54b] TmImageGallery items mouse-only divs → real buttons; focus restore after lightbox close.
- [b32847d0] TmLightbox 390 visual polish vs mockups (component plan).
- [d49c9776] TmLightbox Next button off-screen at 390.
- [f1ebebef] Optional icon chip on mobile action bar tiles.
- [058c3487] MobileActionBar E2E flakes (ForcedVariant_MorePopover, AutoCard sticky).
- [835acf83] F5 follow-ups (1) roving [DONE F4] (2) lightbox/gallery (3) icon chip (4) one-line tile height floor (5) flakes (6) demo safe-area viewport-fit=cover.
- [96b27129] TmSidePanel scope-less fallback uses drawer probed mode.
- [1645e0aa] TmEditorShell MobileSheetSnapPoints / peek snap.
- [ed5296e9] 768 tablet canvas < MinCanvasWidth policy.
- [08a22db5] F6 follow-ups (1) thresholds demo shell binding / trivial E2E (2) tm-focus-trap isTopmost for modal must skip non-modal traps (3) MorePopover flake (4) plan-text corrections da0a2531 phase 8.
- [bdab3cb6] Toolbar core: collapsible NON-button item contract.
- [b6c72292] Toolbar core: Pressed → aria-pressed → TmActionItem.Checked → menuitemcheckbox.
- [4c483f49] Toolbar core: group headers/separators in ActionOverflowMenu.
- [a39625e4] DE-1.2 coordination (blocked on the three above).
- [f4c27b20] F4 follow-ups (1) sheet menu Tab escapes to body (2) TmToolbarOverflowTests '}}' formatting (3) wrapper margin/gap not measured (4) DiagramEditor adoption (5) tm-toolbar-control auto opt-in for TmDropdown triggers in a toolbar.
- [59e85790] + [8b353a98] Scheduler touch MOVE, Alt+arrow keyboard move/resize, aria-live, live target time — "Scheduler plan Fáze 7".
- [32e4e6c4] + [10bcfc90] Private gestures to migrate onto pointer-drag.js (TmSplitterPane eval drag, slider.js, TmDataTable column resize, dashboard.js, workflow-designer.js, tm-sheet.js, TmColorGradient, TmGantt, TmSigningFieldOverlay, DiagramEditor, Wireframe).
- [6de6d5b9] Multi-pointer pinch/pan + auto-scroll core sibling module.
- [449c99df] Neutral cs count labels → Loc.Plural where a sentence form is wanted; Notion relative-time private copies → ITmRelativeTimeFormatter.
- [37682aee] Remove old TmTimeline_*Ago/DateFormat keys one release later.
- [64114d91] InteractiveAutoTests.Scheduler_ViewsWork not re-run (host 7108 not started).
- [8d6b99e3] Shared core ITmUserTimeZone (browser zone) for Scheduler/Gantt/relative time.
- [c88f6e52] Plural demo page with count stepper + E2E.
- [4c92ba05] F7 follow-ups (1) snippet comparison incl. UserTimeProvider body (2) RelativeTimeFormatterTests culture restore (3) optional live-refresh ticker (4) plural demo (5) confirm no private relative-time code in TmNotificationBell; Notion copies.
- [f4057a47] Command palette dark highlight ~1.04:1 + aria-activedescendant.
- [cb3d590f] Signature overlay clear covers 13% of pad at 320.
- [ec0299b9] F8 follow-ups (1) palette li role=group aria-allowed-role (2) default palette scrollable-region-focusable (serious axe) (3) TmIconRegistryTests vs StencilPackRendererTests race → [Collection] (4) E2E copy-button width.
- [4770bcfb] Smoke lane: demo API CORS allow-list only 5010/7106 → smoke fails on any other host.
