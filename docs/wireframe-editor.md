# Wireframe package — `TmWireframeEditor` and friends

`Tempo.Blazor.Wireframe` ships the wireframe editor (`TmWireframeEditor`, `TmWireframeDesignerCanvas`,
`TmWireframeToolbox`, `TmWireframePropertiesPanel`, `TmWireframeLayersPanel`, `TmWireframePagesPanel`, `TmWireframeExportDialog`) and the
stencil renderers behind it. This page records the contracts the redesign plan (`ce19aa8c`) froze phase by phase.

## Reference at a glance (phase 10)

| Topic | Contract |
|-------|----------|
| `PageNavigation` | `Tabs` (default, horizontal page tabs on the canvas) or `Panel` (adds the **Pages** tab to the left panel, `TmWireframePagesPanel`). Opt-in: the default presentation is unchanged. |
| `LayoutMode` / `LayoutBreakpoints` | `Auto` (default) resolves the layout from the editor's **own container** (`data-editor-layout`): desktop ≥ 1200, tablet 768-1199, mobile < 768. `Desktop` / `Tablet` / `Mobile` pin it (tests, forced presentations). Container queries, not viewport media queries, so a narrow editor inside a wide page behaves as a narrow editor. |
| Header slots | `ShowHeader`, `Title`, `HeaderStart`, `HeaderActions` (host actions such as **Open…** / **Save** - the editor has no built-in Open/Save), and on a phone `MobileActions` (action-bar tiles; `ActionOverflow.Never` keeps a tile). |
| Device presets | `WireframeDevicePresets` (Desktop 1440×900, Laptop 1280×800, Tablet 1024×768, iPhone 14 390×844, Android 360×800, Custom) in the inspector's **Canvas** section and the phone "More" menu; every change is the undoable `ResizeCanvasCommand`. |
| `WireframeElement.Name` | Optional human-readable element name (serialised as `name`, omitted when empty; schema and MCP carry it). Edited in the inspector (`RenameElementCommand`, undoable) and shown by the Layers panel and the canvas. |
| Touch and pen | Pointer Events on the shared `pointer-drag.js` / `pointer-gesture.js` helpers: tap selects, drag moves, handles have coarse hit areas (`--tm-wd-handle-hit`), two fingers pinch / pan, a 500 ms hold opens the context menu. A cancelled gesture (Escape, `pointercancel`, a second finger) rolls back and is announced. |
| Non-drag alternatives (WCAG 2.5.7) | toolbox tile: click / tap / Enter / Space; element move / resize: arrow keys and the inspector X / Y / W / H; layers and pages: row menu, F2, Alt+↑ / Alt+↓; zoom: toolbar and the floating zoom buttons. |
| Mobile scope (decision G2) | viewing + light edits (select, move, duplicate, lock, delete, inspector, pages and layers, device presets, PNG / PDF / SVG / JSON export). Resize / rotate handles, rubber band, connectors, align / distribute, grouping, rulers, the minimap and JSON import are **tablet and up**. |
| Stable `data-tm-group` set | `tools | history | arrange | group | align | snap | format | view | zoom | file` (see "Editor shell" - frozen, only additive growth); hosts and tests key off the attribute, never off the toolbar's internal DOM. |
| Standalone canvas hosts | `WireframeCommandStack.Revision` is public and grows on every history change - a host that owns a stack can feed it to `ContentVersion`. A host that renders `TmWireframeDesignerCanvas` itself with `LightEditing="true"` must bump `ContentVersion` after every **in-place** edit of the document (the canvas skips a parent pass whose snapshot - document identity, page, page size, grid, snapping, read-only, scope, class - is unchanged). `TmWireframeEditor` does this for you. |
| Duplicate of a grouped selection | The copies of group members whose container is copied too join the **copied** container; a member duplicated without its container keeps pointing at the original one. |
| Core assets | `wireframe-designer.js` imports `pointer-drag.js` / `pointer-gesture.js` from `_content/Tempo.Blazor/js/` - the host must serve the core package's static web assets (see "Serving the pointer helpers"). Node tests: `npm run test:wireframe`. |
## Read-only and rulers (phase 1)

`ReadOnly` and `ShowRulers` are plain `[Parameter]`s and are **never assigned inside the component** — bind them with
`@bind-ReadOnly` / `@bind-ShowRulers` (the `ReadOnlyChanged` / `ShowRulersChanged` callbacks report the user's toggles),
exactly like the DiagramEditor (see [diagram-editor.md](diagram-editor.md#read-only-di1)):

```razor
<TmWireframeEditor @bind-Document="doc" @bind-ReadOnly="readOnly" @bind-ShowRulers="showRulers" />
```

- The built-in Read-only/Edit toggle drives an internal user state; `IsReadOnly` = `ReadOnly || user toggle` is what the
  canvas (also in JS: `tmWireframeDesigner.setReadOnly`), the properties panel, the layers panel, the context menu and the
  toolbox read.
- With `ReadOnly="true"` and no `ReadOnlyChanged` bound, the toggle is not rendered: the editor is a pinned viewer.
- A read-only editor keeps its toolbox visible but inert: items are `aria-disabled="true"`, muted
  (`tm-wd-toolbox__item--disabled`), not draggable and never raise `OnComponentActivated`.
- Toolbox items are activated by click, tap, Enter (keydown, no auto-repeat) and Space (**keyup**) — the non-drag
  alternative to HTML5 drag-and-drop (WCAG 2.5.7). The new element is centred in the visible canvas area, snapped to the
  grid only when `GridSize > 0`, and clamped **into the page**. A scoped keydown listener on the toolbox cancels the
  browser's default Space scroll only when the target is an item (never for the search box or Tab;
  [keyboard-activation-convention.md](keyboard-activation-convention.md)).

## Design tokens (phase 2)

The editor owns **no palette**. Every `--tm-wd-*` custom property in `_wireframe-editor.css` is an alias of a core
`--tm-*` token, so dark mode (`tokens-dark.css`), the opt-in indigo theme (`data-tm-theme="indigo"`) and consumer
overrides of the core tokens reach the editor with no editor-specific CSS. There is no `[data-theme="dark"]` block and no
`rect[fill="white"]` hack.

| Alias | Core token | Used for |
|---|---|---|
| `--tm-wd-accent` / `-accent-subtle` | `--tm-color-primary` / `-primary-subtle` | active button, selection, handles, minimap viewport |
| `--tm-wd-border` / `-border-focus` | `--tm-border-color` / `-border-color-focus` | hairlines, control borders |
| `--tm-wd-bg-surface` / `-bg-subtle` / `-bg-muted` | `--tm-bg-surface` / `-bg-surface-secondary` / `-bg-muted` | toolbar, panels, hover fills |
| `--tm-wd-text` / `-text-muted` / `-text-light` | `--tm-text-primary` / `-secondary` / `-tertiary` | text |
| `--tm-wd-danger` / `-danger-subtle` / `-danger-text` | `--tm-color-danger` / `-danger-subtle` / `-danger-text` | errors, delete |
| `--tm-wd-canvas-surface` | `--tm-bg-workspace`, tinted 6 % toward `--tm-text-primary` | the workspace behind the page (a faint grey in the default light theme, so the white page stands out; indigo off-white / dark slate) |
| `--tm-wd-canvas-bg` / `-canvas-border` | `--tm-paper` / `--tm-paper-border` | the **page** |
| `--tm-wd-grid-minor` / `-grid-major` | `--tm-paper-border` | grid (minor drawn at 55 % opacity) |
| `--tm-wd-radius` / `-radius-lg` / `-radius-sm` | `--tm-radius-control` / `-radius-card` / `-radius-sm` | corners |
| `--tm-wd-font` | `--tm-font-sans` | typography |
| `--tm-wd-btn-size` | `--tm-space-8` | toolbar button edge (32 px, 8 px rhythm) |

The aliases are declared on `:root`, on the theme boundaries (`[data-theme]`, `[class~="tm-dark"]`, `[data-tm-theme]`) and on
the portaled `.tm-wd-export-dialog` — **never** on an in-editor selector (`.tm-wd-editor`, `.tm-wd-toolbox`, …). A custom
property substitutes its `var()` where it is declared, so a dark region nested below `<html>` needs the aliases re-declared
at its boundary; and `.my-editor .tm-wd-editor { --tm-wd-accent: … }` only reaches the toolbox, props, layers, minimap and
rulers when none of them re-declares the alias (guarded by `WireframeCssContractTests`).

### The page is paper

The stencil components are drawn light-on-white (`#f3f4f6` fills, dark ink), so the page — and the toolbox preview
tiles and the minimap card, which are thumbnails of it — use the theme-invariant `--tm-paper*` tokens, the same
convention as the DiagramEditor page. A dark theme darkens the **chrome and the workspace**, never the page. This is also
what keeps the export honest: `exportSvg` (and therefore PNG/PDF) runs `_bakePaints`, which writes the paints the live
canvas computed (flagged `data-wd-paint`) back as explicit `fill` / `stroke` attributes, so a standalone SVG never
depends on a stylesheet. `WireframePageSvg` (headless render) keeps emitting explicit colours.

To change the page colour for one editor:

```css
.tm-dark .tm-wd-editor { --tm-wd-canvas-bg: var(--tm-bg-surface); }
```

### Stencil tokens (`tempo.stencil.json`)

`token("color.primary", "#3b82f6")` in the pack is a **document-model** token resolved by `StencilTokenResolver`
(element override → document theme → pack theme → pack defaults → literal) into explicit colours at render time — it is
not a CSS variable, and the pack's `themes.dark` table is applied by the host choosing a theme name. The light default
set therefore matches the library's default blue palette; the indigo theme is a chrome/accent theme and does not
re-colour paper-drawn stencils. A host that wants indigo stencils sets the pack tokens through the document theme.

### Stencil themes (`StencilTheme`, plan 643bba06 phase 6)

The pack ships the themes `dark`, `indigo` (primary / info families from the core indigo scale: primary `#6366f1`, subtle `#e0e7ff`, border `#a5b4fc`, text `#3730a3`, info `#4f46e5`) and `indigo-dark` (the dark table with the indigo primary / info family). `TmWireframeEditor`, `TmWireframeDesignerCanvas`, `TmWireframeToolbox` (the preview tiles) and `WireframePageSvgOptions` take **`StencilTheme`** (`string?`, decision `WIREFRAME-STENCIL-THEME`). Resolution order: the `StencilTheme` parameter → the active page `TargetTheme` → the document `TargetTheme` (stored before, now read) → the pack default tokens. `null`, blank, `light` and `default` are the default tokens; an unknown name draws the default tokens and logs ONE warning per name (at most 64 names are remembered, then one "further unknown themes suppressed" line). A name longer than 64 characters or outside `[A-Za-z0-9._-]` is the default tokens too - silently, never stored or logged (`WireframeStencilThemeName.IsValid` is the public rule; the demo API answers `400` for it). The theme-name registry is process-wide: a name known to ANY compiled pack resolves, and a pack that does not declare it keeps its own defaults. Only the stencils change colour: the page stays white paper, in the live canvas and in the exports.

The theme is ambient for the synchronous `RenderSvg` calls (`StencilThemeScope`, internal and async-local, because `WireframeComponentDef.RenderSvg` is the frozen contract between the registry and custom providers); a custom pack with its own `themes` table is themed through the same name, and a pack without that theme keeps its own defaults. `StencilTokenScope` stays internal. **Custom stencil packs: the scope is synchronous only** - it is async-local and wraps the synchronous `RenderSvg` call, so a render function must read the theme (through the pack tokens) without awaiting; an `await` inside it, or handing the work to another thread, would see whichever theme is ambient where it resumes. `RenderSvg` is a synchronous delegate, so a provider that follows its contract is safe by construction. Exports: the browser draws the SVG with the theme before it posts it, so the PNG / PDF server rasterises it as it is; `WireframeExportRequest.Theme` carries the resolved canonical name or `null` for the default tokens (informational: the server never re-themes the SVG; the demo API validates and logs it), and the headless `IWireframeSvgRenderer` / the demo preview endpoints (`?theme=`) resolve the same order (`RenderDocumentAsync` also uses the document `TargetTheme`). The demo passes `indigo` / `indigo-dark` / `dark` from its colour theme and dark mode. The MCP authoring guide documents the themes (`themes` block).

## Icons (phase 2)

Toolbar, properties-panel, toolbox and page-tab controls render `TmIcon` (`IconNames.*`), never emoji or Unicode
arrows. Icon-only buttons keep an `aria-label` / `title`. All icons used exist in core (no local SVGs).

## Layers panel (phase 3)

`TmWireframeLayersPanel` stays a public, standalone component (the editor hosts it as the *Layers* tab of the left panel with `ShowCollapseToggle="false"`). Every
operation has a non-drag alternative (WCAG 2.5.7) and goes through the editor's undoable command stack:

| Operation | Mouse / touch | Keyboard | Command |
|---|---|---|---|
| Set active layer | click the radio | Arrow keys roam and activate (one tab stop) | — (`ActiveLayerIdChanged`) |
| Show / hide, lock / unlock | eye / lock toggle (`aria-pressed`) | Enter / Space | `ToggleLayerVisibilityCommand`, `ToggleLayerLockCommand` |
| Rename | row menu **Rename layer**, double-click | **F2** from any control of the row, Enter / Space on the name | `RenameLayerCommand` |
| Reorder | row menu **Move up / Move down**, drag-and-drop (drop indicator) | **Alt+↑ / Alt+↓** | `ReorderLayersCommand` |
| Delete | trash, row menu **Delete layer** | Tab to either | `RemoveLayerCommand` (never the default layer) |
| Move selection | **Move to layer** select (only with a selection) | — | `MoveElementsToLayerCommand` |

`ReadOnly` disables every one of them (the row menu is not rendered). `ShowCollapseToggle` (default `true`) hides the
panel's own collapse tab for a host that owns the collapse (`.tm-wd-layers--embedded`: no height cap, fills its host).
Rows are 36px, icon buttons 32px; on `(pointer: coarse)` they reach `--tm-touch-target` and the grip is hidden. The
trash is quiet on a fine pointer and shown on row hover, `:focus-within` and for the active row; on a coarse pointer it is
always shown unless the list is narrower than 22rem (the editor column), where Delete lives in the row menu only. Stencil/role audit:
the panel is editor chrome, not a placeable wireframe component, so the stencil pack needs no "layers list" role.

**Default layer.** The default layer is the page's *lowest-ordered* layer, not a fixed one: it cannot be deleted (a deleted
layer's elements fall back to it), and reordering can hand the role to another layer. The panel reads it on every render, so
the Delete controls follow the new lowest layer. The default row's name carries an ria-description ("Default layer").

**Keyboard scope.** Every key typed inside .tm-wd-layers belongs to the panel: the editor's canvas shortcuts
(arrow nudge, Delete/Backspace, Escape, Ctrl+…) ignore events whose target is inside it. Plain arrows on a radio and Space on a
layer name get their browser default (list scroll) cancelled by a scoped listener (initLayersList), never a blanket
preventDefault. Keys inside an open row menu do not reach the row shortcuts. Moves are announced with the new position
("Layer X moved down, position 3 of 5") so repeated moves are re-announced.

## Editor shell (phase 4)

`TmWireframeEditor` renders through the core `TmEditorShell` ([editor-shell.md](editor-shell.md)) and its command row is one core
`TmToolbar` ([toolbar.md](toolbar.md)). The editor resolves its layout from its **own container** (`LayoutMode="Auto"`,
`LayoutBreakpoints` default mobile < 768 / desktop ≥ 1200 since phase 6, `data-editor-layout` on the root); `LayoutMode` can pin a mode for tests and hosts.

| Region | Content |
|---|---|
| `header` (opt-in) | `HeaderStart` · built-in title (or the `Title` slot) · `HeaderActions`. Rendered when `ShowHeader` is set or any slot is supplied. The editor has **no built-in Save or Open** — the host supplies them (the demo puts "Open…" in `HeaderActions`). Without a header the title sits at the start of the toolbar row (today's look). |
| `toolbar` | One `TmToolbar` (`Overflow=Menu`, `Labels=Auto`). Groups carry `role="group"` + `aria-label` and `data-tm-group`: `tools` (select / pan / connector), `history`, `arrange` (backward / forward / lock), `group`, `align` (Align and Distribute dropdowns), `snap`, `format` and `view` ("More" only), `zoom`, `file` (**Export** — pinned, primary — and **Import JSON**, a `Secondary` `TmToolbarFileButton`). |
| `left` | `TmWireframeSidePanel` (internal): tabs **Components** (the toolbox) / **Pages** (only with `PageNavigation=Panel`) / **Layers**. `ShowToolbox` and `ShowPropertiesPanel` keep their names: they gate the Components tab and the inspector + Layers tab. |
| `canvas` | page tabs (`PageNavigation=Tabs`), import banner, rulers, the canvas, the context menu and the minimap card. |
| `right` | `TmWireframePropertiesPanel` (the inspector). |

**Frozen toolbar contract.** The `data-tm-group` set is `tools | history | arrange | group | align | snap | format | view | zoom | file` and is **frozen**:
hosts and tests key off the attribute, never off where a group sits (a group moves between the bar and the "More" menu as the width
changes), and the set only ever grows by an additive, documented entry. If presets are ever added they reuse the DiagramEditor names
(`ToolbarPreset` / `ToolbarGroups`) rather than more `Show*` booleans. The toolbar wrapper carries no `role` / `aria-label` of its own:
the `TmToolbar` inside carries the accessible name. **Export** stays `Pinned`; **Import JSON** is a `Secondary` `TmToolbarFileButton` (decision TOOLBAR-FILE-BUTTON): it is the LAST Secondary item, so it is the first command to leave the bar into "More" - before the arrange actions - and its "More" entry opens the same file chooser (`data-testid="wd-import"` stays on the button, the input is `wd-import-input`).

**"More" menu.** The format painter (Copy style / Paste style / Paste size — also in the inspector's *Format painter* section), the
Read-only toggle (absent while the host pins `ReadOnly="true"`), Show rulers and the Components / Properties panel toggles are
checkable or plain entries of the toolbar's "More" menu (`OverflowOnly`); the bar commands collapse into it by `Priority`
(Secondary first, the later one first; Export and the zoom value are `Pinned`; Import JSON is Secondary). The editor caps the menu to the viewport and scrolls it.
Every former command is still reachable: toolbar, "More", inspector or context menu (guarded by `AllCommands_Reachable`).

**Pages.** `PageNavigation="Tabs"` (default) keeps the horizontal page tabs. `PageNavigation="Panel"` adds the **Pages** tab:
`TmWireframePagesPanel` — a numbered list, click to switch, a "⋯" menu per row (**Rename**, **Duplicate**, **Move up**, **Move down**,
**Delete**; no Delete for the last page), **F2** to rename, **Alt+↑ / Alt+↓** to reorder, HTML5 drag as a pointer extra, and
"+ Add page". It mutates `Document` (page operations are not undoable, like the tabs) and raises `ActivePageChanged` / `DocumentChanged`;
the editor then switches to that page's own undo history. Read-only keeps navigation, drops every edit.
The wireframe keydown guard in `wireframe-designer.js` ignores keys typed inside `.tm-wd-layers` **and** `.tm-wd-pages`.

**Inspector.** With nothing selected: **Canvas** (device preset — Desktop 1440×900, Laptop 1280×800, Tablet 1024×768, iPhone 14 390×844,
Android 360×800, Custom; width / height; **Apply**; all through the undoable `ResizeCanvasCommand`), **Page** (rename the active page) and
**Grid** (show grid, grid size, 0 = snapping off). With a selection, *Element* has **Name** (`RenameElementCommand`, undoable) and
**Delete element** (`RemoveElementsCommand`). There is no *Styles* tab and no generic opacity / corner radius. `ShowGrid` / `GridSize`
follow the never-assigned parameter contract (`ShowGridChanged` / `GridSizeChanged`).

**Element name (WF3).** `WireframeElement.Name` (additive `string?`, JSON `name`): the canvas group gets `aria-label` and a `<title>`
tooltip, the MCP tools (`addElement`, `updateElement`, the author payload, the implementation brief, the authoring guide) and
`wireframe-document.schema.json` know it. Page background and description are deliberately **not** modelled.

**Toolbox.** A 3-column grid of tiles; a tile shows the component's `TmIcon` (`WireframeComponentDef.Icon`) and name, the mini stencil SVG is
the fallback for a missing / undrawable icon. The toolbox re-renders only for its own events or changed parameters (render gate), so a
left-tab switch and opening "More" leave the canvas and the toolbox alone (`RenderCount` tests).
## Touch and pen (phase 5)

The canvas is Pointer Events end to end (a mouse, a finger and a pen). Nothing in the package implements a drag, a long-press or a pinch of
its own: `wireframe-designer.js` attaches sessions of the shared core helpers ([touch-interaction.md](touch-interaction.md)) and only the
pure wireframe maths live in `wireframe-gestures.mjs` (node-tested by `npm run test:wireframe`).

| Gesture | Input | Helper | What it does |
|---|---|---|---|
| Press | mouse / finger / pen | `pointer-drag.js` `attach` | selects the element (Shift = toggle); a tap commits nothing |
| Drag | mouse / finger / pen | `attach` (threshold 5 px) | moves the selection (groups included), grid snap, magnetic guides; one `OnElementMoved` / `OnElementsMoved` on release |
| Resize / rotate / waypoint | mouse / finger / pen | `attach` (threshold 0) | the eight handles, the rotate handle, a connector waypoint; Shift snaps rotation to 15° |
| Rubber band / connector tool / pan tool | mouse / finger / pen | `attach` | selection rectangle, connector drawn onto the element under the pointer (`elementFromPoint`), canvas pan |
| Pinch + two-finger pan | two fingers | `pointer-gesture.js` `attachPinch` | zoom around the fingers, clamped to 0.15×–5×; `onStart` cancels every single-finger session |
| Long-press (500 ms) | touch | `attach` with `longPressMs: 500` | opens the context menu parked beside the finger; the native touch `contextmenu` is ignored so it never opens twice |
| Double-tap | touch / pen | `createDoubleTapTracker` (wireframe-gestures) | the same as a double-click: adds a connector waypoint |
| Middle button, Space+drag, wheel, ctrl+wheel | mouse | plain listeners | pan, pan, zoom, trackpad-pinch zoom |

**Touch targets.** On `(pointer: coarse)` the selection handles are painted 1.5× larger and every handle gets an invisible hit rect
(`[data-handle-hit]`, the same `data-handle` key) of at least `--tm-wd-handle-hit` screen pixels (24 px, `--tm-touch-target` = 44 px
on a coarse pointer), capped so the handles of a small element never overlap; the rects follow every zoom change. A selected connector's
waypoints get a finger-sized transparent circle (`.tm-wd-connector__waypoint-hit`). `.tm-wd-canvas__svg` has `touch-action: none` in CSS
(the helpers are attached with `touchAction: null`), so the page around the editor still scrolls from outside the canvas.

**Cancel and read-only.** Escape, a `pointercancel`, the second finger of a pinch, disposal and a flip to read-only restore the live DOM
(transform, size, rotation, viewBox, connector circles) verbatim and notify .NET of nothing; the pinch and the pan tool keep working
read-only. A rotated element keeps its rotation while it is dragged.

**Non-drag alternatives (WCAG 2.5.7).** Arrow keys nudge the selection (1 unit, Shift = one grid cell, 10 with snapping off; read-only
and locked elements are skipped), the inspector X / Y / W / H fields resize and move (one undoable step), the toolbar zoom buttons are the
pinch alternative, and a toolbox tap / Enter / Space inserts. The toolbox keeps HTML5 drag-and-drop for the mouse; touch inserts by tap.

**Zoom read-out.** `OnZoomChangedEvent` (wheel, trackpad pinch, two-finger pinch, and once after the first render with the real fitted
scale) keeps the toolbar percentage and the rulers in step with the canvas.

## Tablet layout (phase 6)

The editor is a CSS size container (`container-name: tm-wd-editor`) and resolves its layout from its OWN box, never from the viewport: **mobile < 768, tablet 768–1199, desktop ≥ 1200** (the pair `TmDiagramEditor` and the shell docs use for editors; the shell's root sits inside the editor's 1 px border, so a 1280 px editor still measures desktop). A host that embeds the editor in a narrower column therefore gets the tablet layout at any viewport width, and a layout a first measurement railed stays as it was - remove a page sidebar/margin before the app boots when a test needs the desktop layout.

| | Desktop (≥ 1200) | Tablet (768–1199) |
|---|---|---|
| Toolbar | ONE `TmToolbar` (Overflow=Menu) | TWO toolbars: row 1 `tools`, `history`, `arrange`, `group`, `align`, `snap`, `format` (editing); row 2 `view` (Components / Properties toggles, Read-only), `zoom`, `file` (Export, Import) and the document title. Each has its own "More". |
| Left panel (Components / Pages / Layers) | docked shell column (resizable, collapsible) | overlay drawer (the core shell's `TabletPanelPresentation.Overlay`, `data-testid="wd-left-drawer"`), non-modal, opened by `wd-toggle-left` |
| Inspector | docked shell column | non-modal side sheet (`wd-inspector-sheet`) with a close × (`wd-sheet-close`), opened by `wd-toggle-inspector` or by selecting something; it takes its width from the canvas (the page is re-fitted by script), the drawer overlays |
| Minimap | card on the canvas | hidden |
| Toolbox | 3-column tiles | single column of named rows (`TmWireframeToolbox.SingleColumn`) |
| Targets | 32 px | 44 px (`--tm-touch-target`) on `(pointer: coarse)` |

**At most one sheet.** Opening one closes the other; selecting an element opens the inspector and closes the drawer; inserting a component from the drawer closes it (the new element is selected, which opens the inspector). Escape closes the sheet that holds the focus (the shell's non-modal `TmFocusScope`) and returns the focus to the control that opened it - the toolbar toggle, or the canvas when a selection opened the inspector; a toggle-opened sheet moves the focus to its × so Escape works from the keyboard. The canvas shortcut guard ignores keys typed inside `.tm-editor-shell__overlay`. Leaving the tablet layout drops the sheets; the desktop panels are the shell's again.

**No re-render of the canvas.** Opening or closing a sheet calls `TmWireframeDesignerCanvas.NotifyViewportChangedAsync()` (script `relayout`): a pristine view is re-fitted, a moved view keeps centre and zoom, and the call is postponed while a pointer gesture is running (the sheet that a press-selection opens must not shift a drag that has just begun - the svg carries `data-wd-gesture` and the CSS reserves the sheet's room on release).

**One shared implementation.** The tablet sheets are the core ``TmEditorShell`` with ``TabletPanelPresentation="Overlay"`` ([editor-shell.md](editor-shell.md#tablet-overlay-tabletpanelpresentationoverlay), decision ``EDITOR-SHELL-TABLET-PRESENTATION``): the editor keeps passing its ``Left`` / ``Right`` slots on a tablet and binds ``LeftOpen`` / ``RightOpen`` to its sheet flags, the shell renders the drawer / side sheet, traps Escape and restores the focus. The test ids stay ours (``LeftOverlayTestId="wd-left-drawer"``, ``RightOverlayTestId="wd-inspector-sheet"``, ``OverlayCloseTestId="wd-sheet-close"``). ``TmDiagramEditor`` keeps ``Docked`` (its panels fit a tablet) and a floating minimap overlay; the wireframe editor hides its minimap. Sheet behaviours that stay the editor's own: a selection that becomes empty closes the inspector sheet; a press on the canvas background closes the left drawer; the sheet is named by ``aria-labelledby`` its title (a level-2 heading, ``role="heading" aria-level="2"``), the toggles render ``aria-expanded`` (+ ``aria-controls`` while the sheet is open, from the shell's public ``LeftPanelId`` / ``RightPanelId``); and on a coarse pointer the inspector fields, selects, buttons and toggles reach ``--tm-touch-target``.

**No fixed panel widths.** `--tm-wd-toolbox-w` / `--tm-wd-props-w` are gone: desktop widths come from `TmEditorShell` (260 / 300 px, resizable), the tablet sheets from `--tm-wd-sheet-w` (20rem, handed to the shell as `--tm-editor-shell-overlay-width`; at most 60 % of the canvas).

## Mobile layout (phase 7)

In an editor container **below 768 px** (`data-editor-layout="mobile"`) the editor is **viewing + light edits** (plan decision G2). It renders the core
`TmEditorShell` mobile presentation instead of squeezing the desktop chrome (on a phone the canvas used to measure 0 px wide):

| | Mobile (< 768) |
|---|---|
| Header | the document title (always) plus `HeaderStart` / `HeaderActions`, then the **tool row** (`data-testid="wd-mobile-tools"`): Select, Pan, Undo, Redo and its own "More" (Forward / Backward for the selection). The shell drops its `Toolbar` slot on a phone while mobile actions exist, so the row lives in the header. |
| Canvas | the whole width; no rulers, no minimap; a **floating zoom** (`data-testid="wd-zoom-overlay"`: `-`, `%`, `+`, fit - one row over the canvas, outside the svg). |
| Action bar | `TmMobileActionBar` (`MaxVisible` 3 + More): **Add** (the palette, `data-action-id="components"`), **Pages & layers** (`pages-layers`), **Export** (`export`); host actions from `MobileActions` follow (a host action pinned `ActionOverflow.Never`, e.g. "Save", keeps a tile). **More** holds the device presets (undoable `ResizeCanvasCommand`), the grid and Read-only toggles and JSON / SVG export. There is no built-in Save or Open and no Import. |
| Sheets | one non-modal bottom sheet (snap points 35 % / 70 % / 100 % of the stage = peek / half / full) with the shell tabs **Add** \| **Properties** \| **Pages** (the Pages \| Layers segment is the shell's `Extra` tab, the Layers tab is the side panel's). The sheet starts closed. Tapping the active action-bar tile closes it. Each tab starts at its top (the shell scrolls the shared sheet body back when the tab changes - `TmEditorShell`, not the wireframe script). **Focus:** closing the sheet puts the focus back on the action-bar tile that OPENED it (`data-action-id`; the first tile when a selection opened it or the tile moved into "More"), only when the focus was really lost. |
| Selection | selecting an element **peeks the Properties sheet**: its type / name and **Duplicate / Lock / Delete** (`wd-quick-actions`, 44 px, one undoable step each) above the full inspector (Name, size, props - one swipe up). A sheet the user already had open on the inspector keeps its tab and snap; the sheet a selection opened closes when the selection empties; a tap on an already selected element brings it back. A selection that peeks the sheet is announced through the canvas' polite live region ("Selected: {name}. Properties and actions are open." - `TmWireframe_AnnounceSelectionPeek`, en / cs / sk / fr); moving the selection while the inspector is already open announces nothing new. |
| Canvas gestures | tap selects, a finger drags the element (grid snap), two fingers pinch / pan, a one-finger drag on the empty canvas **pans** (no rubber band), a tap on it deselects, a 500 ms hold opens the **action sheet** (`TmWireframeContextMenu.Presentation=Sheet`: `TmOverlayPanel` bottom sheet with Done; align / distribute / group are not offered). The lift click of the hold is swallowed once (the sheet opens under the finger) - only a POINTER click (`detail > 0`); a keyboard or screen-reader activation (`detail === 0`) always reaches its target. |
| Not on a phone | resize / rotate handles, rubber band, the connector tool and waypoint handles (`TmWireframeDesignerCanvas.LightEditing` -> `tmWireframeDesigner.setLightEditing`), align / distribute, grouping, rulers, the minimap and JSON import: **tablet and up**. |
| Export dialog | `TmWireframeExportDialog.MobilePresentation=Auto` (default): a **bottom sheet** on a phone (`TmModal` `Sheet`, `FooterLayout=Inline`: Cancel and Export side by side, sticky), the centred dialog elsewhere. No local sheet is built. |

**No re-render of the canvas.** Opening, switching a tab of and closing a sheet never repaints the SVG scene: `TmWireframeDesignerCanvas` gates a parent render
(`LightEditing` only) on a snapshot of its parameters (document identity, active page, the page width / height, grid, read-only, snapping, component scope, class, `LightEditing`) and `ContentVersion`. The editor derives that token from `WireframeCommandStack.Revision` (every push - also a coalesced move -, undo, redo and clear) plus one counter for the changes that bypass a command (a host parameter pass, a replaced or imported document, a layout flip; `MarkContentChanged()`), so a command path has nothing to remember. A document mutation therefore still repaints (`RenderCount` bUnit tests, a `MutationObserver` E2E on the svg). The script re-measures the canvas after a sheet moves
(`relayout`), and the shell reserves the sheet's lowest snap as padding under the canvas, so a selected element stays visible above the peeked sheet.

**Download on iOS Safari.** `tmWireframeDesigner.downloadFile` saves through a `Blob` URL on the hidden anchor (`URL.createObjectURL`, revoked after 60 s - iOS Safari hands the blob to a new tab asynchronously, so a one-second revoke could kill the download); the base64
`data:` URL is only the fallback for a host without `Blob`. Large `data:` URLs (a PNG / PDF export is easily a few MB) are dropped by iOS Safari. iOS Safari has no real
download: the link with the `download` attribute opens the file in a new tab (PNG / PDF / SVG / JSON are all viewable) and the user saves it from the share sheet - a platform
limit, not something the component can change.

**Layout anchors:** `wd-mobile-tools`, `wd-zoom-overlay`, `wd-zoom-in|out|fit`, `wd-quick-actions`, `wd-quick-duplicate|lock|delete`,
`.tm-mobile-action-bar__action[data-action-id="components|pages-layers|export"]`, `.tm-wd-editor__zoom-overlay`, `.tm-wd-quick-actions`, `[data-light-editing]` on `.tm-wd-canvas-wrap`.
## Phase 5 carry-forward (phase 6)

### Context menu keyboard and focus
Shift+F10 and the ContextMenu key (while the canvas wrapper or the svg has focus and the target is not an input) open the context menu through the same `_openContextMenuAt` as the right-click and the touch long-press: at the centre of the first selected element (or connector), or - with nothing selected - at the middle of the canvas as the canvas menu. When the menu opens, focus moves to its first enabled `menuitem` (disabled items are skipped); Arrow Up / Down rove with wrap-around, Home / End jump, Tab and Escape close it. Escape, Tab and choosing an item return the focus to the canvas wrapper (`.tm-wd-canvas-wrap`); a press outside the menu closes it without stealing the focus back. The menu root has `tabindex="-1"` as the focus fallback when every item is disabled. The `.tm-wd-editor__context-*` selectors are unchanged.

### Gesture rollback announcement
When a move, resize, rotate or waypoint gesture is rolled back by the user (Escape, `pointercancel`, a second finger starting a pinch) the canvas announces "Move cancelled" (`TmWireframeCanvas_GestureCancelled`, localised en/cs/sk/fr) in a visually hidden `role="status" aria-live="polite"` region (`.tm-sr-only`, `data-testid="wd-canvas-status"`). A normal gesture end, `destroy`, the read-only flip and the long-press hand-over stay silent. The script calls `[JSInvokable("OnGestureCancelled")]`; the component alternates a trailing no-break space so an identical message is announced again on a repeat.

### Long-press cue
While a touch hold is pending (press -> 500 ms) the svg carries `tm-wd-canvas__svg--holding` and a ring (`.tm-wd-hold-cue`, 24 screen px radius) grows at the finger over 500 ms. It disappears on lift, cancel, drift beyond the tolerance and when the menu opens. `prefers-reduced-motion: reduce` shows a static ring instead of the animation. Styles are the last section of `_wireframe-editor.css`; colours are `--tm-wd-accent` only.

### Middle-button pan exception
The mouse-only middle-button pan is the one gesture not built on `pointer-drag.js` (the helper starts only from the primary button). `_cancelMiddlePan` gives it the helper's cancel contract: Escape and `destroy` restore the viewBox, release the capture, remove the move/up/cancel listeners and reset the cursor. Recorded in docs/touch-interaction.md.

### Serving the pointer helpers
`wireframe-designer.js` is a classic script that dynamic-imports `pointer-drag.js` and `pointer-gesture.js` from `_content/Tempo.Blazor/js/` (resolved from its own URL: `../../Tempo.Blazor/js/`) and `wireframe-gestures.mjs` from its own folder. A host that does not serve the core package's static web assets under `_content/Tempo.Blazor/js/` gets NO pointer gestures (a console error "the pointer helpers ... could not be loaded"; keyboard, wheel and toolbar keep working). The failed import is not cached, so the next editor retries.

### Relayout on container resize
`tmWireframeDesigner.relayout(svg)` re-measures the svg when its container changes size (a side panel opening / closing, a window resize, a device rotation) WITHOUT a Blazor render. A view the user has not moved / zoomed yet (`inst.userViewChanged === false`) is re-fitted to the page; a moved or zoomed view keeps its centre and its zoom. It reports `OnViewBoxChanged` + `OnZoomChanged` back to .NET and refreshes the coarse handle hit areas. A `ResizeObserver` on the svg and the wrapper triggers it (one run per animation frame, guarded for hosts without `ResizeObserver`; disconnected in `destroy`). `TmWireframeDesignerCanvas.NotifyViewportChangedAsync()` is the explicit trigger for hosts that change the layout themselves (the editor calls it after a panel opens / closes). The first-render fit is `applyInitialZoom` (see "Initial zoom" below); a pristine view re-applies the initial mode on resize.

### Initial zoom (`InitialZoom`, plan 643bba06 phase 6)

`TmWireframeEditor.InitialZoom` / `TmWireframeDesignerCanvas.InitialZoom` (`WireframeInitialZoom { Fit = 0, ActualSize = 1 }`, decision `WIREFRAME-INITIAL-ZOOM`) replace the svg's own `meet` fit. `tmWireframeDesigner.applyInitialZoom(svg, mode)` lays the view out at the first render, when the `Document` instance, the active page or its size changes and when the mode changes (the canvas keeps a layout key; a parent render that changes none of these never touches the view). `Fit` = the whole page with a 40 px padding, shrunk only: a page that fits at 100 % stays at exactly 100 % and is centred. `ActualSize` = 100 %, the page starts at its padded top-left corner when it is larger than the canvas. A zoom the user chose is never overwritten by anything automatic: `relayout` (container resize) keeps the centre and the zoom of a moved view and re-applies the INITIAL mode to a pristine one. The Fit toolbar button / context-menu entry is the explicit way back. The zoom read-out reports the scale the script returns. Blazor writes the page-sized `viewBox` only when the page SIZE changed, never on an unrelated parent render. `exportSvg` writes the page (`viewBox="0 0 W H"`, explicit width / height) so the live view never leaks into a file.

### Export to another origin (`ExportHttpClientName`)

The PNG / PDF export posts to `api/wireframe/export/png|pdf`. `ExportHttpClientName` names an `IHttpClientFactory` client (empty = the injected `HttpClient`, i.e. the host origin). Unlike `TmDiagramEditor.ExportHttpClientName` (which throws), a missing factory or a client without a base address falls back to the injected client and logs ONE warning per editor instance - the WebAssembly demo and the InteractiveAuto client name `DemoApi` instead of getting `405` from their own origin.

### Handle gestures beside an open inspector sheet

A press that selects on a tablet opens the inspector sheet, and the canvas reserves its room only after the lift (`data-wd-gesture`, so the svg never resizes under a drag that just began). A gesture that starts with the sheet ALREADY open must keep that reservation: the script reads the computed padding BEFORE it sets `data-wd-gesture` and marks `data-wd-reserved`, and the stylesheet drops the reservation only while a gesture runs without that marker. Without it the svg widened under the finger and a resize-handle drag ran in a moved coordinate system (`Touch_ResizeHandle(1024)`: a 360x240 button became 224x328). A handle press also counts as an active gesture from the PRESS (`inst.resize` / `rotate` / `waypoint`), not only from the drag threshold.

## E-mail components as stencils (EMAIL-WIREFRAME-ROLES, plan 643bba06 phase 8)

Four public components of `Tempo.Blazor.EmailTemplates` are placeable on a wireframe page and ship a declarative stencil in the `tempo` pack plus a schema in `BuiltInComponentSchemas` and a role: `TmEmailTemplateEditor` (`email-template-editor`), `TmEmailCanvasBlock` (`email-block`), `TmEmailItemList` (`list-view`) and `TmEmailItemDialog` (`dialog`). Every other public `TmEmail*` component is editor chrome (toolbox, inspector, import / export / validation dialogs, sub-editors): it has no stencil of its own, but its type name is a synonym of a role in the vocabulary (`TmEmailTemplateToolbox` -> `email-template-editor`), so every component still resolves a role. `EmailComponentsStencilTests` derives the component list by reflection, so a new public `TmEmail*` component without a role fails the test. The decision row is in `planning/tm-email-template-editor-2.10.md` (EMAIL-WIREFRAME-ROLES).

## E2E anchors (stable selectors)

`.tm-wd-editor`, `.tm-wd-editor__toolbar`, `[data-region="left|canvas|right|toolbar|header"]`, the core `.tm-toolbar` /
`.tm-toolbar-btn` / `button.tm-toolbar-more` and `[data-tm-group]`, `data-testid`s `wd-export`, `wd-export-json|svg|png|pdf`, `wd-align`,
`wd-distribute`, `wd-import`, `wd-device-preset`, `wd-canvas-width|height|apply`, `wd-page-name`, `wd-grid-show|size`, `wd-element-name`,
`wd-delete-element`, `wd-page-add`, `wd-page-<id>`, `wd-pages-panel`, `wd-section-canvas|page|grid|selection`,
`.tm-wd-toolbox__item[data-component-type]`, `.tm-wd-canvas__svg`, `.tm-wd-canvas-wrap`, `.tm-wd-props`, `.tm-wd-layers`, `.tm-wd-pages`,
`.tm-wd-export-dialog`, `#wd-export-format`, `#wd-export-filename`; mobile: see "Layout anchors" above; tablet: `wd-toggle-left`, `wd-toggle-inspector`, `wd-left-drawer`, `wd-inspector-sheet`, `wd-sheet-close`, `wd-toolbar-row-1|2`, `wd-canvas-status`.
