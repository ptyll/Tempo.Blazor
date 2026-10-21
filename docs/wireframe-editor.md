# Wireframe package — `TmWireframeEditor` and friends

`Tempo.Blazor.Wireframe` ships the wireframe editor (`TmWireframeEditor`, `TmWireframeDesignerCanvas`,
`TmWireframeToolbox`, `TmWireframePropertiesPanel`, `TmWireframeLayersPanel`, `TmWireframeExportDialog`) and the
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

## E2E anchors (stable selectors)

`.tm-wd-editor`, `.tm-wd-editor__toolbar`, `.tm-wd-editor__btn` (`--icon`, `--active`, `--text`),
`.tm-wd-editor__dropdown(-item)`, `.tm-wd-toolbox__item[data-component-type]`, `.tm-wd-canvas__svg`,
`.tm-wd-canvas-wrap`, `.tm-wd-props`, `.tm-wd-layers`, `.tm-wd-export-dialog`, `#wd-export-format`,
`#wd-export-filename`.
