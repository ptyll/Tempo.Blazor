# Wireframe redesign — phase 2 UX review (tokenisation, icons, visual layer)

Captured by `WireframePhase2ThemeE2ETests` (1440 px; light/dark x default/indigo): `editor--*`, `export-dialog--*` here and
`open-dialog--*` in `../../document-library/phase2/`. Compared with `output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/`
(`wireframe-editor--desktop.png`, `wireframe-export-dialog--desktop.png`, `wireframe-open-document--desktop.png`).
**Structure is deliberately unchanged** (grouped labelled toolbar, left page/layer panel, Properties | Layers tabs, device presets
are phases 4+); this phase judges colour, corners, density and dark/indigo behaviour only.

| # | Finding | Verdict |
|---|---------|---------|
| 1 | Toolbar: every emoji/glyph is a stroked `TmIcon`; buttons are 32 px with a hairline hover/active state - matches the reference's icon style and density. Labels under icons (reference) are a toolbar-phase item. | Fixed (icons); labels later |
| 2 | Dark mode: toolbar, toolbox, inspector, dialogs are slate surfaces with no light plates. The old white toolbox thumbnails on dark tiles (baseline #5) now sit on an intentional paper tile; the minimap card is paper. | Fixed |
| 3 | The wireframe PAGE stays white in dark mode on purpose (stencils are drawn light-on-paper; the export matches the screen). The reference mockup draws the same. | By design, documented |
| 4 | Indigo: accent (selection, active tool, badge, open-dialog selection, primary buttons) turns `#4f46e5` light / `#818cf8` dark; the workspace turns off-white `#f7f8fc` / deep slate `#0b1120`. The open dialog no longer hard-codes indigo. | Fixed |
| 5 | Review defect: in the default light theme the page edge was nearly invisible (white page on white workspace). Added a 1 px page drop-shadow, and the workspace (`--tm-wd-canvas-surface`) is `--tm-bg-workspace` tinted 6 % toward the text colour so the white page stands out from it. | Fixed in this phase |
| 6 | Review defect: the native radios of the open dialog ignored the theme (browser blue). `accent-color: var(--tm-color-primary)`. | Fixed in this phase |
| 7 | Review defect: minimap viewport tint was an opaque accent-subtle slab in dark. Now 10 % accent mix. | Fixed in this phase |
| 8 | Open dialog selected row is a primary inset bar + tint (baseline #13 "selection too faint"), the selected card a 2 px primary ring. | Fixed |
| 9 | Still open (later phases, per plan): footer hint wraps next to the buttons at 1440, "New folder" wraps to two lines, no Recent group/preview pane, minimap overlaps the canvas corner, right column mixes properties + layers. | Deferred (WF1/WF4, phases 4/8) |