# DiagramEditor package — `TmDiagramEditor` and friends

`Tempo.Blazor.DiagramEditor` ships the diagram editing surface: the `TmDiagramEditor` shell
(header, command toolbar, toolbox, canvas, inspector, status bar), the `TmDiagramCanvas` SVG
engine, the dialogs (connect, SQL/CSV import, template gallery), the floating panels (search,
minimap, layers) and the JS modules (`diagram-editor.js`, `diagram-editor-touch.js`,
`dagre.min.js` + the d3 force/hierarchy bundles).

```
<link rel="stylesheet" href="_content/Tempo.Blazor/css/tempo-blazor.css" />
<link rel="stylesheet" href="_content/Tempo.Blazor.DiagramEditor/css/tempo-blazor-diagram-editor.css" />
<script type="module" src="_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.js"></script>
```

`diagram-editor.js` is an ES module that imports `diagram-editor-touch.js` (which imports the core
`pointer-drag.js` / `pointer-gesture.js`) and `dagre.min.js`; host pages load it once, everything
else is pulled by the module graph.

## Responsive scope (G2)

The editor resolves its presentation from its own container (see
[responsive-conventions.md](responsive-conventions.md)); the shell's default breakpoints are
768 / 1200 px and the resolved layout is mirrored on the editor root as `data-editor-layout`:

| Layout | Container width | What the user gets |
|---|---|---|
| **Desktop** | ≥ 1200 | Full redesign chrome: header (page tabs, title, panel toggles, "⋯" view menu), one command toolbar row, docked toolbox / inspector, status bar, minimap card. Full editing: resize/rotate handles, edge drawing, align/distribute, auto layout, imports. |
| **Tablet** | 768–1199 | Same docked panels through `TmEditorShell` (at most one side panel expanded, the other rails), touch gestures from the shared pointer helpers, 44px finger handles (`--tm-diagram-handle-hit`), **floating minimap overlay** over the canvas corner, long-press context menu. Full editing stays available. |
| **Mobile** | < 768 | **Viewing + light edits only (G2).** Canvas plus a non-modal bottom sheet (peek / half / full) holding *Shapes* and the *Properties \| Layers* inspector, the F5 action bar (Select / Pan / Properties tiles, export-only "More"), floating zoom pill and minimap, long-press context menu as a bottom sheet. Light edits: selection, node text/style in the properties, layer visibility/lock/active/"move to layer", delete, undo/redo and tap-insert from the palette. **Resize/rotate, edge drawing, align/distribute, layout and import stay tablet+.** A `Viewer` preset or a pinned read-only host renders no action bar. |

`TmDiagramCanvas.ShowTransformHandles` / `ShowConnectArrows` (both default `true`) are the knobs a
mobile host uses to drop the reshaping / edge-drawing affordances; the editor sets them from the
resolved layout itself.

## Toolbar contract (stable public API — frozen since phase 1)

Nested hosts (the Modeling preview, custom toolbars around the editor) must key off the toolbar
**contract**, never off positional or structural CSS selectors on the toolbar's internal DOM. The
contract is three pieces:

1. **`ToolbarPreset="DiagramToolbarPreset.*"`** — the group *set*:
   - `Full` (default) — every group. Since the phase-4 restructure the command groups share one
     `TmToolbar` row, search + panel toggles live in the header, grid/snap/page/ruler toggles live
     in the header "⋯" menu, and zoom + read-only live in the status bar. The preset names the set
     of commands, **not** a screen region.
   - `Viewer` — preview-friendly subset: `zoom`, `view`, `search`, `export`. The read-only toggle
     never renders. The Modeling preview uses exactly
     `ReadOnly="true" ToolbarPreset="DiagramToolbarPreset.Viewer"`.
2. **`ToolbarGroups="DiagramToolbarGroups.*"`** — optional flags override (`null` = by preset).
   Hide a group (or show a Viewer group you need) without reordering anything.
3. **`data-tm-group` attributes** — every group's DOM carries one stable value from the frozen set:

   `history | clipboard | tools | format | insert | arrange | search | panels | zoom | view |
   canvas-size | layout | export | templates | import | readonly`

   Style or locate groups with `[data-tm-group='zoom']` scoped from the editor root
   (`[data-testid='diagram-editor']`), never with `:nth-child`, sibling order or internal class
   chains. Where a group renders (header / toolbar row / status bar / overflow menu) is a
   **phase-4 layout detail** that may change again; the attribute values and the group set are
   the contract. Commands can collapse into the `TmToolbar` "More" menu (`Overflow=Menu`) at
   narrow widths — a host that needs a specific command always visible gives it `Priority`
   through the editor's toolbar configuration, and E2E helpers click the More menu as fallback.

Host action buttons (save/share per G9) go through the `HeaderStart` / `HeaderActions`
`RenderFragment` slots, which render at the start / end of the first (header) toolbar row.

### Read-only (DI1)

`ReadOnly` is a plain `[Parameter]` and is **never assigned inside the component**. The built-in
Read-only/Edit toggle drives only the internal `_userReadOnly` state, exposed as
`IsReadOnly` (`ReadOnly || _userReadOnly`) and propagated to the JS canvas. When the host sets
`ReadOnly="true"` the toggle hides itself and the editor stays read-only no matter what the user
does. Effective read-only blocks every editing path: toolbar, keyboard shortcuts, context menu,
imports, templates, paste and JS.

## Inspector (`InspectorLayout`, DI2)

`InspectorLayout="DiagramInspectorLayout.Tabs"` (default) renders the properties and layers
panels as **Properties | Layers** tabs in one column; `Stacked` opts back into the legacy
column with both panels stacked. `ShowPropertiesPanel` / `ShowLayersPanel` keep controlling
whether a panel exists at all; a lone panel renders without the tab strip.

## Minimap (`MinimapPlacement`, DI3)

`MinimapPlacement="DiagramMinimapPlacement.*"`:

- `Auto` (default) — panel card in the right inspector column on desktop, floating overlay over
  the canvas corner on tablet and mobile (the tablet/mobile DOM is
  `.tm-diagram-editor__minimap-overlay`; host CSS that pinned the card must cover both roots or
  pin the placement).
- `Panel` — always the card. `Overlay` — always the floating overlay.

Pages stay as tabs at the top in every layout (DI3); there is no page drawer.

## Panels and chrome

| Region | Desktop | Tablet / mobile |
|---|---|---|
| Pages | Header tab strip | Same tab strip (wraps/clips on 320) |
| Document title | Header input (`ShowTitle`, disabled while read-only) | Hidden on mobile |
| Command toolbar | One `TmToolbar` row, overflow "More" menu | F5 action bar tiles on mobile |
| Toolbox | Left docked panel | Shapes tab of the mobile sheet |
| Inspector | Right docked column, tabs per `InspectorLayout` | Properties \| Layers tabs of the sheet |
| Minimap | Card or overlay per `MinimapPlacement` | Floating overlay |
| Zoom | Status bar group + `__zoom-label` | Floating zoom pill |
| Search | `Ctrl+F` / toolbar group, floating panel | Same floating panel |
| Context menu | Viewport-anchored menu | Long-press bottom sheet (shared F3 sheet) |
| Status bar | `ShowStatusBar` (read-only toggle lives here) | Not rendered |

Docked panels resize/collapse through `TmEditorShell` (strip toggles, rails); on tablet the shell
rails the non-expanded side while `LeftOpen`/`RightOpen` keep meaning *shown*.

## Touch

Touch interactions run on the shared core helpers — `pointer-drag.js` (single-pointer drags with
long-press) and `pointer-gesture.js` (two-finger pinch/pan) — imported by the package's
`diagram-editor-touch.js`; see [touch-interaction.md](touch-interaction.md). Diagram specifics:

- Resize/rotate handles render an invisible hit area sized by `--tm-diagram-handle-hit`
  (24px, 44px on coarse pointers) so fingers can grab them.
- Pinch zooms and pans the canvas (`viewBox`-driven); a host that needs custom pinch behaviour
  cancels the drag from the pinch's `onStart`.
- Long-press opens the context menu (as a bottom sheet on tablet/mobile); a tap is press/release
  only — tap-time behaviour hooks `onPress`, never `onEnd`.
- Toolbox tiles insert on tap; dragging a tile still performs drag-and-drop insertion.

## E2E anchors (stable selectors)

`data-testid="diagram-editor"` on the root; `.tm-diagram-editor__zoom-label`, `__toolbar`,
`__props-wrap`; the layers panel toggle keeps its accessible name "Toggle layers"; mobile sheet
content is reachable through `aria-label` on the shared overlay-panel sheet. `data-tm-group`
values above are the stable toolbar anchors.
