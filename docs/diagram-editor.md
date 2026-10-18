# DiagramEditor package — `TmDiagramEditor` and friends

`Tempo.Blazor.DiagramEditor` ships the diagram editing surface: the `TmDiagramEditor` shell
(header, command toolbar, toolbox, canvas, inspector, status bar), the `TmDiagramCanvas` SVG
engine, the dialogs (connect, SQL/CSV import, template gallery), the floating panels (search,
minimap, layers) and the JS modules (`diagram-editor.js`, `diagram-editor-touch.js`,
`dagre.min.js` + the d3 force/hierarchy bundles).

```
<link rel="stylesheet" href="_content/Tempo.Blazor/css/tempo-blazor.css" />
<link rel="stylesheet" href="_content/Tempo.Blazor.DiagramEditor/css/tempo-blazor-diagram-editor.css" />
<script src="_content/Tempo.Blazor.DiagramEditor/js/dagre.min.js"></script>
<script src="_content/Tempo.Blazor.DiagramEditor/js/d3-dispatch.min.js"></script>
<script src="_content/Tempo.Blazor.DiagramEditor/js/d3-timer.min.js"></script>
<script src="_content/Tempo.Blazor.DiagramEditor/js/d3-quadtree.min.js"></script>
<script src="_content/Tempo.Blazor.DiagramEditor/js/d3-hierarchy.min.js"></script>
<script src="_content/Tempo.Blazor.DiagramEditor/js/d3-force.min.js"></script>
<script src="_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.js"></script>
```

`diagram-editor.js` is a classic script (it defines `window.tmDiagramEditor`). It loads
`diagram-editor-touch.js` with a dynamic `import()` resolved from its own URL, and that module
imports the core `pointer-drag.js` / `pointer-gesture.js`. `dagre` and the d3 bundles are globals:
load them before it. Auto layout warns and does nothing without them.

## Responsive scope (G2)

The editor resolves its presentation from its own container (see
[responsive-conventions.md](responsive-conventions.md)); the shell's default breakpoints are
768 / 1200 px and the resolved layout is mirrored on the editor root as `data-editor-layout`:

| Layout | Container width | What the user gets |
|---|---|---|
| **Desktop** | ≥ 1200 | Full redesign chrome: header (page tabs, title, panel toggles, "⋯" view menu), one command toolbar row, docked toolbox / inspector, status bar, minimap card. Full editing: resize/rotate handles, edge drawing, align/distribute, auto layout, imports. |
| **Tablet** | 768–1199 | Same docked panels through `TmEditorShell` (at most one side panel expanded, the other rails), touch gestures from the shared pointer helpers, 44px finger handles (`--tm-diagram-handle-hit`), **floating minimap overlay** over the canvas corner, long-press context menu. Full editing stays available. |
| **Mobile** | < 768 | **Viewing + light edits only (G2).** Canvas plus a non-modal bottom sheet (peek / half / full) holding *Shapes* and the *Properties \| Layers* inspector, the F5 action bar (Select / Pan / Properties tiles, export-only "More"), floating zoom pill and minimap, long-press context menu as a bottom sheet. Light edits: selection, node text/style in the properties, layer visibility/lock/active/"move to layer", delete, undo/redo and tap-insert from the palette. **Resize/rotate, edge drawing, align/distribute, layout and import stay tablet+.** A host-made viewer — pinned `ReadOnly="true"` or a toolbar group set without editing groups (e.g. the `Viewer` preset) — renders no action bar or panels sheet; the user's own Read-only toggle keeps them (read-only panels). |

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
     `ReadOnly="true" ToolbarPreset="DiagramToolbarPreset.Viewer" ShowTitle="false"`.
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
`RenderFragment` slots, which render at the start / end of the header bar.

### Read-only (DI1)

`ReadOnly` is a plain `[Parameter]` and is **never assigned inside the component**. The built-in
Read-only/Edit toggle drives only the internal `_userReadOnly` state, exposed as
`IsReadOnly` (`ReadOnly || _userReadOnly`) and propagated to the JS canvas. When the host sets
`ReadOnly="true"` the toggle hides itself and the editor stays read-only no matter what the user
does. Effective read-only blocks every editing path: toolbar, keyboard shortcuts (the Select/Pan
`KeyV`/`KeyH` shortcuts stay live), context menu, imports, templates, paste and JS. Read-only
keeps navigation (pan, pinch, zoom) and inspection: a click or tap selects a node or an edge for
the read-only properties panel, which shows a plain summary (shape name, text/data fields,
position/size, fill/stroke; label, routing, source/target for edges) instead of the editing
widgets; nothing is moved, resized, rotated or connected. Note for keyboard users: with the focus
in the toolbox palette column, `Space` may scroll the palette column as well as activating the
focused tile (the column is scrollable; a tile keeps its own `Space` activation on keyup).

## Inspector (`InspectorLayout`, DI2)

`InspectorLayout="DiagramInspectorLayout.Tabs"` (default) renders the properties and layers
panels as **Properties | Layers** tabs in one column; `Stacked` opts back into the legacy
column with both panels stacked. `ShowPropertiesPanel` / `ShowLayersPanel` keep controlling
whether a panel exists at all; a lone panel renders without the tab strip: with only one panel
available it renders alone in `__right-wrap`, and the header carries a single
**Inspector** toggle whose icon/label follow the lone panel's content.

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
| Context menu | Viewport-anchored menu | Tablet: anchored finger-sized menu, offset from the finger (`data-anchor="touch"`, items ≥ `--tm-touch-target`); mobile: long-press bottom sheet (shared F3 sheet) |
| Status bar | `ShowStatusBar` (read-only toggle lives here) | Not rendered |
| All of the above (`ShowToolbar`) | `ShowToolbar="false"` keeps its pre-redesign meaning: it hides every built-in control that used to live in the toolbar — the header bar's built-in controls (title, mobile history, search and panel toggles, "⋯" menu), the command row and the status bar (and on mobile the zoom pill and the action bar). Page tabs, panels, canvas, minimap and the `HeaderStart`/`HeaderActions` host slots stay. | Same rule |

Docked panels resize/collapse through `TmEditorShell` (strip toggles, rails); on tablet the shell
rails the non-expanded side while `LeftOpen`/`RightOpen` keep meaning *shown*. A node selection
always re-raises the inspector with the **Properties** tab active — even when the user had railed
it after switching to Layers (the selection pins the tab; the palette returns to its rail).

## Touch

Touch interactions run on the shared core helpers — `pointer-drag.js` (single-pointer drags with
long-press) and `pointer-gesture.js` (two-finger pinch/pan) — imported by the package's
`diagram-editor-touch.js`; see [touch-interaction.md](touch-interaction.md). Diagram specifics:

- Resize/rotate handles render an invisible hit area sized by `--tm-diagram-handle-hit`
  (24px, 44px on coarse pointers) so fingers can grab them. On a coarse pointer the n/e/s/w
  mid-edge resize hits do not render (they would sit exactly on the port hit circles); the four
  corners keep the full resize affordance.
- A **selected** node's ports get finger-sized invisible hit circles (`.tm-diagram-port-hit`,
  same `data-port-id`, rendered after the transform-handles group so they win the paint order;
  pointer-active only on a coarse pointer — on desktop they stay inert so the mid-edge resize
  handles and the small visible ports keep the mouse): a finger drag from a port draws an edge.
  **Occlusion rule:** the port only claims a press whose raw target is the port/hit circle itself
  or an edge corridor painting over it — a port buried under another control (a resize handle, a
  node body) is covered, not pressed. Connection points get NO hit circle: a stencil's connection
  points sit exactly on the resize handles (the default rectangle set is 4 corners + 4 midpoints +
  centre), so they stay reachable through the mouse-sized visible circle and the edge-corridor hit
  stack. Without `ShowConnectArrows` (mobile) no ports, connection points or hit circles render at
  all — no invisible dead zone on the touch surface.
- **Decorator pane paint order (review fix): buttons > gesture hit areas > visual handles.** Each
  selected node renders, in order, (1) `g.tm-diagram-node-handles` (the visible resize/rotate
  handles + their enlarged hit areas), (2) `g.tm-diagram-port-hits` (the finger-sized port hit
  discs) and (3) `g.tm-diagram-connect-arrows` (the quick-connect arrow buttons). A press lands on
  the topmost painted element — parking the arrows inside the handles group made the port hit discs
  cover them on a coarse pointer, so the arrows were unreachable by finger. Now they paint last and
  stay reachable; on a coarse pointer the CSS gives each arrow a 44px hit area (`--tm-diagram-connect-hit`,
  a `::before` inset) and offsets the wrap outward by `handle-hit/2 + gap` so the expanded hit
  clears the port hit discs (the N arrow also shifts sideways, clear of the rotate hit above the top
  edge). The JS transform sync keeps every `[data-node-id]` group glued to the node during drags,
  and a live resize hides the port hits and the connect arrows (stale positions). One more
  foreignObject fact: Chromium never synthesizes a compatibility click for a touch that starts in
  `foreignObject` content, so the touch module carries a dedicated arrow-tap session — a stationary
  press/release clicks the button (opening the connect dialog) and swallows the native click that
  follows, while a moved press stays a drag and never clicks.
- Pinch zooms and pans the canvas (`viewBox`-driven); a host that needs custom pinch behaviour
  cancels the drag from the pinch's `onStart`.
- Long-press opens the context menu — **mobile only** as the F3 bottom sheet; on tablet and
  desktop it stays an anchored `position:fixed` menu that a finger can hit: opened by touch it
  carries `data-anchor="touch"` and is parked above the finger (flipping below at the top edge,
  `x + 16`, then clamped), its items are at least `--tm-touch-target` tall, a press ring grows
  under the finger during the hold, and the menu opens exactly once. **Release-click guard:** the
  compatibility click of the lifting finger is swallowed (armed per hold, plus a 350 ms window
  after the lift, scoped to this editor's menu), so the release never activates a menu item the
  user never pressed. A tap elsewhere while the menu is open dismisses it (`pointerdown` capture —
  touch taps included), as does starting a pan, a pinch or an edge draw.
- A tap is press/release only — tap-time behaviour hooks `onPress`, never `onEnd`.
- Toolbox tiles insert on tap; dragging a tile still performs drag-and-drop insertion.

## E2E anchors (stable selectors)

`data-testid="diagram-editor"` on the root; `.tm-diagram-editor__zoom-label`, `__toolbar`,
`__props-wrap`; the header toggles carry
`data-testid="diagram-editor-toggle-toolbox|inspector|minimap"`; mobile sheet content is
reachable through `aria-label` on the shared overlay-panel sheet. `data-tm-group`
values above are the stable toolbar anchors.
