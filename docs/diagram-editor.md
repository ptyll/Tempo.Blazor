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
<script src="_content/Tempo.Blazor.DiagramEditor/js/diagram-arrow-select.js"></script>
<script src="_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.js"></script>
```

`diagram-editor.js` is a classic script (it defines `window.tmDiagramEditor`). It loads
`diagram-editor-touch.js` with a dynamic `import()` resolved from its own URL (falling back to
`document.baseURI`), and that module imports the core `pointer-drag.js` / `pointer-gesture.js`.
`dagre` and the d3 bundles are globals: load them before it — auto layout warns and does nothing
without them.

## Responsive scope (G2)

The editor resolves its presentation from its own container (see
[responsive-conventions.md](responsive-conventions.md)); the shell's default breakpoints are
768 / 1200 px and the resolved layout is mirrored on the editor root as `data-editor-layout`:

| Layout | Container width | What the user gets |
|---|---|---|
| **Desktop** | ≥ 1200 | Full redesign chrome: header (page tabs, title, panel toggles, "⋯" view menu), one command toolbar row, docked toolbox / inspector, status bar, minimap card. Full editing: resize/rotate handles, edge drawing, align/distribute, auto layout, imports. |
| **Tablet** | 768–1199 | Same docked panels through `TmEditorShell` (at most one side panel expanded, the other rails), touch gestures from the shared pointer helpers, 44px finger handles (`--tm-diagram-handle-hit`), **floating minimap overlay** over the canvas corner, long-press context menu. Full editing stays available. |
| **Mobile** | < 768 | **Viewing + light edits only (G2).** Canvas plus a non-modal bottom sheet (peek / half / full) holding the flat *Shapes | Properties | Layers* tabs (sheet closed at load — the action bar, a selection or `MobileSheetInitiallyOpen` opens it), the F5 action bar (Select / Shapes / Properties tiles, Pan and exports in "More"), the floating horizontal zoom pill and the opt-in minimap (the header "⋯" menu toggle), long-press context menu as a bottom sheet. Light edits: selection, node text/style in the properties, layer visibility/lock/active/"move to layer", delete, undo/redo and tap-insert from the palette. **Resize/rotate, edge drawing, align/distribute, layout and import stay tablet+.** A host-made viewer — pinned `ReadOnly="true"` or a toolbar group set without editing groups (e.g. the `Viewer` preset) — renders no action bar or panels sheet; the user's own Read-only toggle keeps them (read-only panels). |

**Mobile sheet.** The sheet starts **closed** at load; the action bar's *Shapes* tile opens it on
the shapes tab (the *Properties* tile on the properties tab). Selecting a node on mobile scrolls
the canvas so the selection peeks above the sheet (the reveal is gesture-guarded so a drag never
triggers it). The node/edge actions list keeps **Delete as a sticky bottom row** in the danger
style, and on a coarse pointer the action labels drop the keyboard-shortcut hints (a finger has no
shortcut keys). The sheet and the action bar disappear only for a **host-made viewer** (A4) — the
user's own Read-only toggle keeps both, in their read-only form.

`LayoutMode` (`Auto` default) overrides the measured band: `Desktop` / `Tablet` / `Mobile` pin the
presentation (e.g. for tests and kiosk hosts), and `LayoutBreakpoints` replaces the default
768 / 1200 thresholds. The wireframe stencil exposes the same choice as its `layoutMode` prop,
where `auto` always renders the desktop variant.

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

**The `import` group is deliberately NOT a `TmToolbarFileButton`** (decision
`DIAGRAM-IMPORT-DROPDOWN-STAYS`, plan 643bba06). It is one Pinned `TmDropdown`
(`[data-tm-group='import'] .tm-dropdown-trigger`) holding Import JSON (a `<label>` around a hidden
`InputFile`, which opens the chooser natively), Import SQL and Import CSV. `TmToolbarFileButton`
exists for a file chooser that must open from a collapsible bar button *and* its "More" entry; the
group never collapses, and moving JSON out of the menu would split the frozen group contract.

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

**Header (X3).** Since 2.9.1 the header carries ONE **Inspector** toggle instead of separate
Properties and Layers toggles (X3 breaking change): it shows/hides the whole inspector and the
tabs choose the content. Below 1024px of *editor* width (a container query on the editor root,
not the whole tablet band) the header toggles render **icon-only** — the visible name stays as
`aria-label` and the tooltip in `title`.

## Layers panel (row contract)

Each layer row is one `<li>` carrying, in order: the drag handle (reorder by drag, disabled while
a rename is in progress), the visibility eye toggle, the active-layer radio, the name — a
`<span tabindex="0" role="button">` that renames on double-click or Enter, takes the width
priority and truncates with an ellipsis — the non-interactive lock **indicator** (Lock/Unlock
lives in the row menu; a locked row keeps the indicator so the state stays visible) and the ⋯ row
menu. The row menu is the **core `TmContextMenu`** (D4/X5), not a hand-rolled popup: it owns
Escape/outside dismissal, arrow roving, the mobile bottom sheet and focus return. Its trigger is
the `.tm-diagram-layers__row-menu-trigger` span; `MobilePresentation` follows the resolved layout
(`RowMenuPresentation`: bottom sheet on mobile, anchored menu elsewhere) and after rename, move or
delete the panel returns focus to the trigger through `TmContextMenu.FocusTriggerAsync` (the rows
carry `@key`, so the DOM survives the operation). The menu offers Rename, Lock/Unlock, Move
up/down, **Move selected nodes here** (only with a selection, disabled when every selected node
already lives on the row's layer) and Delete (danger, hidden for the last/default layer).

The same "move the selection to a layer" action **also** appears as the panel's bottom
*Move to layer* `TmSelect` — the two are intentional duplicates with different ergonomics: the
select is the discoverable bulk path (pick any target once), the row-menu item is the contextual
shortcut (the target is the row under your finger). Both run the same undoable
`MoveNodesToLayerCommand`.

## Minimap (`MinimapPlacement`, DI3)

`MinimapPlacement="DiagramMinimapPlacement.*"`:

- `Auto` (default) — panel card in the right inspector column on desktop, floating overlay over
  the canvas corner on tablet and mobile (the tablet/mobile DOM is
  `.tm-diagram-editor__minimap-overlay`; host CSS that pinned the card must cover both roots or
  pin the placement). The overlay width follows the resolved layout — 200px card, 160px tablet
  overlay, 112px mobile — and the same value drives the tap/drag navigation mapping, so host CSS
  must never resize the minimap svg. The map frames the diagram content (the union of all VISIBLE
  node boxes, the edge geometry — waypoints, free ends, label anchors — and the current viewport,
  padded) instead of the whole page; nodes and edges on hidden layers are not mapped. The
  viewport rectangle is a transparent outline drawn under the node boxes. The tablet overlay
  collapses in place (`.tm-diagram-editor__minimap-overlay-collapse`, in a header strip that
  never covers the map) and reopens from the floating `.tm-diagram-editor__minimap-overlay-reopen`
  button; on mobile the minimap starts hidden and the header "⋯" menu carries its toggle. The
  default-hide applies ONLY while that toggle exists — `ShowToolbar && ShowMinimap && (Panels ||
  View group)` — so a `ToolbarGroups` selection without Panels/View keeps the overlay visible
  (hiding it would leave no reopen path); a Viewer preset with `ShowMinimap` keeps the toggle
  reachable through the View group. A viewport drag freezes the pointerdown-time bounds/scale
  (the render and the client→svg mapping keep them, so pan feedback cannot rescale the map under
  the finger), captures the pointer and clamps to the frozen bounds.
- `Panel` — always the card. `Overlay` — always the floating overlay.

Pages stay as tabs at the top in every layout (DI3); there is no page drawer.

## Panels and chrome

| Region | Desktop | Tablet / mobile |
|---|---|---|
| Pages | Header tab strip | Same tab strip, horizontally scrollable on narrow widths |
| Document title | Header input (`ShowTitle`, disabled while read-only) | Same input in the compact header (flexes to the free width; `ShowTitle`) |
| Command toolbar | One `TmToolbar` row, overflow "More" menu | F5 action bar tiles on mobile |
| Toolbox | Left docked panel | Shapes tab of the mobile sheet |
| Inspector | Right docked column, tabs per `InspectorLayout` | Flat sheet tabs Shapes \| Properties \| Layers (TmEditorShell third panel) |
| Minimap | Card or overlay per `MinimapPlacement` | Tablet: ~160px collapsible overlay, visible by default. Mobile: hidden, toggled from "⋯" |
| Zoom | Status bar group + `__zoom-label` | Floating zoom pill |
| Search | `Ctrl+F` / toolbar group, floating panel | Same floating panel |
| Context menu | Fixed menu at the pointer, clamped into the editor box or the viewport (whichever is its containing block); long-press on nodes, edges and empty canvas on touch | Same menu: tablet anchors it finger-sized and offset from the finger (`data-anchor="touch"`, items ≥ `--tm-touch-target`); mobile renders it as the long-press bottom sheet (shared F3 sheet) |
| Status bar | `ShowStatusBar` (read-only toggle lives here) | Tablet: same status bar. Mobile: not rendered (zoom → floating pill, Read-only → "⋯" menu) |
| All of the above (`ShowToolbar`) | `ShowToolbar="false"` keeps its pre-redesign meaning: it hides every built-in control that used to live in the toolbar — the header bar's built-in controls (title, mobile history, search and panel toggles, "⋯" menu), the command row and the status bar (and on mobile the zoom pill and the action bar). Page tabs, panels, canvas, minimap and the `HeaderStart`/`HeaderActions` host slots stay. | Same rule |

Docked panels resize/collapse through `TmEditorShell` (strip toggles, rails); on tablet the shell
rails the non-expanded side while `LeftOpen`/`RightOpen` keep meaning *shown*. A node selection
always re-raises the inspector with the **Properties** tab active — even when the user had railed
it after switching to Layers (the selection pins the tab; the palette returns to its rail).

## Embedded viewers (Fáze 9)

A host that embeds the editor as a **viewer** (the Modeling preview and its "Open in editor"
dialog) drives it through these additive knobs — never through CSS on the editor's internal DOM
(the `Preview_css_has_no_selectors_on_diagram_editor_internals` guard). Every default is today's
behavior, so a host that sets nothing sees no change:

| Knob | Type / default | Effect |
|---|---|---|
| `ShowPageTabs` | `bool`, `true` | `false` drops the `.tm-diagram-editor__page-tabs` row in EVERY layout (a single-page preview has nothing to switch). Page switching stays available by setting the document's own `ActivePageIndex` (the host owns the `Document` reference) — the row is removed, not hidden with CSS. |
| `FitToViewAsync(mode)` | `DiagramFitMode mode = Page` | Public zoom-to-fit (final UX review M5): asks the JS engine for a zoom-to-fit and re-renders the zoom label. `DiagramFitMode.Page` (default) is today's both-axes fit; `DiagramFitMode.Width` scales by the CONTAINER WIDTH only — a taller-than-box diagram overflows (scrolls) instead of shrinking. `TmDiagramCanvas.FitToView(mode)` mirrors it: ONE canvas fit path applies the page-view margin and reports the FINAL scale (after the margin) to the zoom label. |
| `FitOnDocumentLoad` | `DiagramFitMode?`, `null` | Fits ONCE after the first render of a NEW document identity. The same document never re-fits; a replaced document does; user zoom afterwards wins. `Width` is the embedded mobile preview's first-load fit. The fit stays OWED across a canvas remount (a mobile↔docked flip remounts `TmDiagramCanvas`): the editor re-arms it on the finally mounted canvas, which fits exactly once. |
| `ShowMobileZoomOverlay` | `bool?`, `null` | The floating − \| % \| + \| fit pill rule: `null` keeps today's rule (`IsMobileLayout && ShowToolbar &&` the Zoom group); `true` shows the pill on a mobile layout even chrome-less (`ShowToolbar=false`); `false` never shows it. The docked bands keep the status-bar zoom. |
| `MinimapInitiallyCollapsed` | `bool`, `false` | Seeds the minimap card (desktop) / floating overlay (tablet) collapsed ONCE at initialization. The user's own toggle wins afterwards; the mobile X1 rule (minimap hidden, "⋯" toggle) is unchanged. The Modeling tablet dialog seeds it from the editor's resolved mode. |
| `PreferredStencilIds` | `IReadOnlyList<string>?`, `null` | Fáze 15 (the Modeling "Open in editor" notation palette): stencil ids pinned as the FIRST toolbox library — order preserved, unknown ids ignored — while every other library starts collapsed (a live search still expands matches across the WHOLE library). `null` keeps today's toolbox (a set→null transition restores exactly the palettes the pinned pass collapsed; the user's own collapses survive). The pinned section carries `data-library-id="notation-palette"` and `data-pinned="true"`; its label renders ONCE — on the single palette's own collapsible category header, never as a repeated library header. On a mobile layout the pinned library is the "first category" for `CollapseCategoriesExceptFirst`. A pinned stencil that also belongs to a native library is listed in BOTH places (the pinned row for the notation context, the native row for its category) — that duplication is intentional; inserting from either creates the same stencil. |
| `PreferredStencilsLabel` | `string?`, `null` | Label of the pinned library (the Modeling dialog passes the notation profile's display name), rendered on the pinned palette's category header. Null falls back to the localized `TmDiagramToolbox_PreferredDefault`. Ignored while `PreferredStencilIds` is unset. |

### `TmDiagramStencilIcon` (Fáze 15)

The public small stencil preview (`StencilId`, `Size`, `Class`) — the same text-trimming pipeline
the toolbox tiles use, for hosts that present stencils outside the toolbox (the Modeling palette
and legend embed it instead of duplicating the SVG). Always `aria-hidden="true"`: the surrounding
row owns the visible label. A stencil id the registry does not know renders the generic fallback
shape (`data-fallback="true"`; the edge kind falls back to the edge glyph). `Size` is a CSS box
size applied to width and height (default `32px`).

```razor
<TmDiagramStencilIcon StencilId="bpmn2.task.user" Size="1rem" />
```

`diagram-editor.js` `fitToView(container, padding, mode)` keeps its two-argument contract: the
third `mode` argument is additive (`'width'`), so a host calling the old signature gets today's
page fit. The width mode scales by `containerWidth / contentWidth` (padding included) capped by
`FIT_MAX_SCALE`, TOP-aligns shorter-than-box content (`minY - padding` is the visible top edge —
the content pins to the band's top instead of floating centered) and keeps `minX - padding` as
the visible left edge. `addPageViewMargin` reports the zoom AFTER the margin (the final, smaller
scale) through `OnZoomChanged`.

```razor
@* A chrome-less mobile viewer with zoom buttons and a fit-to-width first load *@
<TmDiagramEditor Document="@doc"
                 ReadOnly="true"
                 ToolbarPreset="DiagramToolbarPreset.Viewer"
                 ToolbarGroups="DiagramToolbarGroups.Zoom"
                 ShowPageTabs="@(doc.Pages.Count > 1)"
                 ShowToolbar="false"
                 ShowMobileZoomOverlay="true"
                 FitOnDocumentLoad="DiagramFitMode.Width" />

@* A tablet-dialog editor whose floating minimap starts collapsed *@
<TmDiagramEditor Document="@doc" MinimapInitiallyCollapsed="@isTablet" />
```

## Escape ordering (capture consumers)

The editor is frequently layered inside a host overlay (a `TmModal` dialog). The core
`tm-focus-trap.js` honors Escape on a document **bubble** keydown listener, so every editor Escape
consumer registers in the **capture** phase on `document` and runs before any bubble listener —
the ordering against the trap is deterministic, not registration-order dependent. A consumer that
actually handles the Escape marks it (`preventDefault()` + `e.tmEscapeHandled = true`, both
honored by the trap and by later consumers).

**One press closes exactly ONE layer — the topmost one** (confirmed ordering, Fáze 10):

1. **The custom context menu** — while open (`_menuOpen`) it owns the Escape through its own
   capture-phase key handler, and the other consumers SKIP (`_onSearchEscape` returns early), so
   a search panel or view menu never closes under it. The menu's handler marks the event the
   same way.
2. **The open "⋯" view menu** (stamped `data-open`): Escape closes the menu — through the
   additive `TmDiagramCanvas` parameter `OnCloseViewMenu` — and outranks the search panel.
3. **The open search panel**: Escape closes it through `OnCloseSearch` (the editor closes the
   panel and refocuses the canvas container so the focus does not drop to `body`). A host overlay
   behind the editor stays open. Both consumers only fire while the focus is inside the editor
   root, so an Escape typed elsewhere is never stolen.
4. **An inline edit** (the `.tm-diagram-edge-label-input` foreignObject input, a
   `.tm-diagram-node__inline-input`/`-textarea` section editor, a swimlane header or a table
   cell): the Escape is CONSUMED — the edit CANCELS (the value reverts, nothing is pushed to the
   undo stack) and a host dialog stays open; the NEXT Escape continues with the order above. The
   canvas' capture consumer resolves the owner — a node editor through its registered
   `TmDiagramStencilShape` (`CancelActiveEditFromJs`), everything else through the canvas'
   `[JSInvokable] OnInlineEditEscape` (`CancelEdgeLabelEdit`). The Blazor `@onkeydown` Escape
   branches on the inputs themselves stay as idempotent fallbacks. After the cancel the focus
   returns to the canvas container (the JS node branch focuses `container` with
   `preventScroll`; the edge-label path goes through `FocusCanvasAsync`) — the edit input
   unmounts and the focus must not drop to `body`. Because both capture listeners see the SAME
   event, `_onKeyDown` skips a handled Escape entirely (a `defaultPrevented || tmEscapeHandled`
   guard is the first line of its Escape branch) — the inline-edit cancel never ALSO clears a
   non-empty selection on the same keypress.
5. **A running gesture or a non-empty selection** (rotate, pending/drawing edge, waypoint or
   whole-edge drag): the canvas key handler consumes the Escape and cancels the session.
6. **Nothing to consume** (empty canvas, no session): the canvas handler does NOT call
   `preventDefault()` — the Escape keeps bubbling, which is what lets the host dialog close.
   (Before the fix the canvas consumed every Escape unconditionally, and whether the editor or a
   host dialog won depended on listener registration order.)

The custom context menu is flagged open SYNCHRONOUSLY (`inst._menuOpen = true` before the
deferred placement `setTimeout`), so an Escape typed between the open call and the placement
already sees `_menuOpen` and cannot close a surface under the still-opening menu.

## Motion

Under `prefers-reduced-motion: reduce` the search-match pulse, template-card hover lift,
connect-arrow/swatch scale, layers-panel width and colour transitions are disabled (a static
highlight replaces the pulse). `ReducedMotionSelectorTests` scans this package's stylesheets for
both the media query and the motion-suppressing rules.

## Canvas tokens (paper and ink)

The page itself — and everything painted on it — is theme-invariant: the page rect, edge-label
pills and node bodies resolve the core `--tm-paper*` tokens (`--tm-paper`, `--tm-paper-ink`,
`--tm-paper-ink-muted`, `--tm-paper-border`, `--tm-paper-backdrop`), which are deliberately
declared light-only (NOT re-declared in `tokens-dark.css`) so the paper stays white and the ink
stays dark grey in every theme. Hosts rebrand them by targeting `:root`, `[data-theme="dark"]`
and `.tm-dark` together, or per-canvas through the aliases below.

The one paper-adjacent surface that DOES follow the theme is the area OUTSIDE the page: the
`.tm-diagram-page-backdrop` rect paints `--tm-diagram-backdrop` — light keeps the historical
paper-backdrop gray (an alias of `--tm-paper-backdrop`, declared in `tokens.css`), and the dark
theme roots re-map it to the workspace tone (`tokens-dark.css`), so the white page floats on a
dark canvas instead of inside a light frame (carry-forward F8 m5). Hosts restyle the backdrop by
overriding `--tm-diagram-backdrop` (per canvas like the aliases below, or on the theme roots);
overriding `--tm-paper-backdrop` still works but restyles every surface aliased to it.

Everything painted ON the page (node bodies, edge-label pills, cardinalities) resolves two
canvas-scoped aliases declared on `.tm-diagram-canvas`:

- `--tm-diagram-paper` — the page fill (alias of the theme-invariant `--tm-paper`, white in
  every theme). Edge-label pills reuse it so the edge stroke never runs through the text.
- `--tm-diagram-node-ink` — the node/label ink (alias of `--tm-paper-ink-muted`).

A host that rebrands the diagram surface (e.g. sepia paper) overrides BOTH tokens by targeting
`.tm-diagram-canvas` or any deeper wrapper around one canvas — the declaration lives on the
canvas element itself, so an override needs no `!important` and must not be scoped to a
shallower ancestor than `.tm-diagram-canvas`:

```css
.my-diagram-host .tm-diagram-canvas {
    --tm-diagram-paper: #f5eeda;   /* sepia paper */
    --tm-diagram-node-ink: #4a3f2a;
}
```

`exportViewportAsSvg` copies the computed fills/colours of `.tm-diagram-page`,
`.tm-diagram-page-backdrop` and `.tm-diagram-edge-label__text` onto the serialized clone, so
an export taken under a host override carries the overridden paints.

## Touch

Touch interactions run on the shared core helpers — `pointer-drag.js` (single-pointer drags with
long-press) and `pointer-gesture.js` (two-finger pinch/pan) — imported by the package's
`diagram-editor-touch.js`; see [touch-interaction.md](touch-interaction.md). Diagram specifics:

- Resize/rotate handles render an invisible hit area sized by `--tm-diagram-handle-hit`
  (`var(--tm-space-6)` = 24px, `var(--tm-touch-target)` = 44px on coarse pointers) so fingers can
  grab them. On a coarse pointer the n/e/s/w
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
  cancels the drag from the pinch's `onStart`. Zoom is clamped to a single shared range
  (0.15–5.0, `DiagramZoomLimits`) on every path — wheel, pinch, `zoomTo`, fit-to-view and the
  zoom buttons — and a gesture past a limit clamps exactly to it instead of freezing.
- On tablet an edge draws from a finger/pen drag starting on a port (or the edge corridor of a
  connection point); on mobile only the connect dialog creates edges (G2).
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
values above are the stable toolbar anchors. Selectors removed in 2.9.1 (`__body`,
`__minimap-wrap`, `__toolbar-row`, `__toolbar-spacer`, `tm-toolbar-btn--active`, …): see
CHANGELOG 2.9.1 Breaking / Migration.
