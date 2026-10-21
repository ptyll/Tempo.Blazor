# Wireframe redesign — phase 1 "before" baseline: UX findings

Captured by `WireframeResponsiveScreenshotTests` (editor empty / selected, export dialog, layers panel) and
the document-open captures in `../../document-library/baseline/`. Sizes: 1440×900, 1024×768 touch, 390×844,
320×640; light + dark. Compared against `output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/`
(`wireframe-editor--desktop.png`, `wireframe-export-dialog--desktop.png`, `wireframe-open-document--desktop.png`).

**This phase records gaps only — the visual work belongs to the later phases named in the last column.**

| # | Screen | Gap vs. reference / defect observed | Severity | Addressed by |
|---|--------|-------------------------------------|----------|--------------|
| 1 | Editor, desktop | One 20-button toolbar row (tools, undo/redo, arrange, align, snap, painter, zoom, canvas size, export, import, read-only, panel toggles) — it overflows to the right at 1440 (the panel toggles are clipped); the reference has a grouped toolbar with labels and an overflow menu. | High | Toolbar primitive phase (F4 base plan) |
| 2 | Editor, desktop | Page tabs ("Page 1", "+", "Dup") are a separate strip; the reference shows pages/layers in the left panel (opt-in `PageNavigation=Panel`). | Medium | Page/layer panel phase |
| 3 | Editor, desktop | Right column mixes properties, "Format painter", layers and "Move to layer" in one scroller; the layers header is cut off and the minimap card overlaps the canvas corner. Reference has Properties | Layers tabs. | High | Inspector phase |
| 4 | Editor, desktop | Canvas is a flat grey well with a dense grid; no device-preset chips, no zoom pill in a status bar (reference has both). | Medium | Canvas / device presets phase |
| 5 | Editor, dark | Toolbox preview tiles keep white fills (Money Display, Calendar Heatmap are white boxes on dark) and the minimap card stays light — the SVG-fill inversion only covers `rect[fill="white"]`. | Medium | Dark-mode / token phase |
| 6 | Editor, 1024 touch | Same desktop layout; toolbox, canvas and props compete for 1024 px, touch targets in the toolbar are ~32 px (< 44 px). | High | Responsive shell phase |
| 7 | Editor, 390 / 320 | Canvas collapses to **0 px** wide (measured: `canvas width = 0px`): the 220 px toolbox + 280 px props are fixed. Nothing editable is reachable; the Export button is clipped outside the viewport. Reference mobile = viewing + light edits with a bottom sheet. | Blocker (mobile) | Shell + bottom-sheet phases (G2) |
| 8 | Layers panel | Capture is a 279×132 sliver: header, list and "Move to layer" are squeezed under the properties panel; the list itself is not visible without scrolling. Reference shows a full layer list with eye/lock/handle. | High | Page/layer panel phase |
| 9 | Export dialog | Works and now preselects PDF. Gap: no PNG/PDF segmented choice, no format-dependent options summary, scale hidden for PDF without explanation (reference shows both columns). | Low | Export dialog phase |
| 10 | Export dialog, 320/390 | Dialog is a centred modal; on phones it should be a bottom sheet (`TmModal.MobilePresentation`). | Medium | Dialog phase (F2) |
| 11 | Open document, desktop | Functional, but the reference adds a "Recent" group, item counts and a preview pane; selected row is only a faint tint. | Low | Open-dialog redesign (WF4) |
| 12 | Open document, 390 / 320 | Tree + list share one row; the footer "Stays in sync with the original document." hint collapses into a one-character-wide column (vertical text), the primary "Open" button is clipped at the right edge. | Blocker (mobile) | Open-dialog redesign (WF4) |
| 13 | Open document, grid | Cards have no visible selected border in dark theme and (until this phase) no keyboard focus — focus ring added, selection contrast remains weak. | Low | Open-dialog redesign (WF4) |
| 14 | All, keyboard | Focus ring uses the local `--tm-wd-border-focus` (`#3b82f6`) instead of the core `--tm-focus-ring`; layers controls and page tabs got the ring in this phase. | Low | Token phase (phase 2 of this plan) |
