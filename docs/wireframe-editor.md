# Wireframe package — `TmWireframeEditor` and friends

`Tempo.Blazor.Wireframe` ships the wireframe editor (`TmWireframeEditor`, `TmWireframeDesignerCanvas`,
`TmWireframeToolbox`, `TmWireframePropertiesPanel`, `TmWireframeLayersPanel`, `TmWireframePagesPanel`, `TmWireframeExportDialog`) and the
stencil renderers behind it. This page records the contracts the redesign plan (`ce19aa8c`) froze phase by phase.

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
`LayoutBreakpoints` default mobile < 640 / desktop ≥ 860, `data-editor-layout` on the root); `LayoutMode` can pin a mode for tests and hosts.

| Region | Content |
|---|---|
| `header` (opt-in) | `HeaderStart` · built-in title (or the `Title` slot) · `HeaderActions`. Rendered when `ShowHeader` is set or any slot is supplied. The editor has **no built-in Save or Open** — the host supplies them (the demo puts "Open…" in `HeaderActions`). Without a header the title sits at the start of the toolbar row (today's look). |
| `toolbar` | One `TmToolbar` (`Overflow=Menu`, `Labels=Auto`). Groups carry `role="group"` + `aria-label` and `data-tm-group`: `tools` (select / pan / connector), `history`, `arrange` (backward / forward / lock), `group`, `align` (Align and Distribute dropdowns), `snap`, `format` and `view` ("More" only), `zoom`, `file` (**Export** — pinned, primary — and Import). |
| `left` | `TmWireframeSidePanel` (internal): tabs **Components** (the toolbox) / **Pages** (only with `PageNavigation=Panel`) / **Layers**. `ShowToolbox` and `ShowPropertiesPanel` keep their names: they gate the Components tab and the inspector + Layers tab. |
| `canvas` | page tabs (`PageNavigation=Tabs`), import banner, rulers, the canvas, the context menu and the minimap card. |
| `right` | `TmWireframePropertiesPanel` (the inspector). |

**"More" menu.** The format painter (Copy style / Paste style / Paste size — also in the inspector's *Format painter* section), the
Read-only toggle (absent while the host pins `ReadOnly="true"`), Show rulers and the Components / Properties panel toggles are
checkable or plain entries of the toolbar's "More" menu (`OverflowOnly`); the bar commands collapse into it by `Priority`
(Secondary first, the later one first; Export, Import and the zoom value are `Pinned`). The editor caps the menu to the viewport and scrolls it.
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
## E2E anchors (stable selectors)

`.tm-wd-editor`, `.tm-wd-editor__toolbar`, `[data-region="left|canvas|right|toolbar|header"]`, the core `.tm-toolbar` /
`.tm-toolbar-btn` / `button.tm-toolbar-more` and `[data-tm-group]`, `data-testid`s `wd-export`, `wd-export-json|svg|png|pdf`, `wd-align`,
`wd-distribute`, `wd-import`, `wd-device-preset`, `wd-canvas-width|height|apply`, `wd-page-name`, `wd-grid-show|size`, `wd-element-name`,
`wd-delete-element`, `wd-page-add`, `wd-page-<id>`, `wd-pages-panel`, `wd-section-canvas|page|grid|selection`,
`.tm-wd-toolbox__item[data-component-type]`, `.tm-wd-canvas__svg`, `.tm-wd-canvas-wrap`, `.tm-wd-props`, `.tm-wd-layers`, `.tm-wd-pages`,
`.tm-wd-export-dialog`, `#wd-export-format`, `#wd-export-filename`.
