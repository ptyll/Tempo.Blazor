# Wireframe phase 6 baselines

Produced by `WireframePhase6TabletE2ETests` (tablet drawers / inspector sheet, 1024 + 1366, light + dark) and `WireframePhase6ZoomThemeE2ETests` (demo 1440 / 1920, fit zoom, StencilTheme) with `TM_E2E_UPDATE_BASELINE=1`. Plan 643bba06 phase 9. Never copy by hand.


## Intentional byte-identical pairs

Two producing tests capture the same unselected, light, 1024 px tablet view under their own names, so these files have identical content and are kept on purpose (each test reads back its own name; merging them would couple two specs):

- `tablet-1024--drawer-components.png` = `tablet-1024-light--drawer-components.png` (`WireframePhase6TabletE2ETests` tablet matrix vs `Screenshots_Tablet_1024_LightAndDark`)
- `tablet-1024--toolbar-light.png` = `tablet-1024-light--empty.png` (same two tests; the empty tablet editor is the toolbar shot)

