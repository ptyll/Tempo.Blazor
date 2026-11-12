# Tempo Stencil Format Specification

This document is the product contract for Tempo-owned stencil packs. It is aligned with:

- `src/Tempo.Blazor.Wireframe/wwwroot/tempo-stencil.schema.json`
- `src/Tempo.Blazor.Wireframe/wwwroot/wireframe-document.schema.json`
- `src/Tempo.Blazor.Abstractions/Wireframe/Stencil/*`
- `src/Tempo.Blazor.Wireframe/Components/Wireframe/*`

## Scope

`tempo-stencil` is a declarative, framework-agnostic JSON format for reusable wireframe components. The format describes catalog metadata, props, tokens, optional themes, and a safe SVG render tree. PromptHelper app stencil packs are uploaded through PromptHelper UI/API and are not reflected from CodeLibrary.

Built-in Tempo components are supplied by the trusted built-in `tempo` pack. App packs are user/application data and must render declaratively.

## Stencil Pack Root

A stencil pack root is a JSON object with these fields:

- `format`: must be `tempo-stencil`.
- `formatVersion`: current version is `1`.
- `id`: pack id. The built-in pack id is `tempo`; PromptHelper app packs use their stored pack id.
- `namespace`: logical pack namespace. The built-in namespace is `tempo`. App pack components are exposed as `app:{id}:{localType}` by the registry.
- `isBuiltIn`: true only for the trusted built-in Tempo pack.
- `target`: optional portability metadata: `framework`, `library`, and `version`.
- `tokens`: string token map used by `token()`.
- `themes`: named token override maps.
- `icons`: named SVG path data map.
- `parts`: reusable `RenderNode` snippets.
- `components`: component definitions.

The built-in pack component count is not hardcoded in the format. Tests derive the expected built-in count from `new BuiltInComponentSchemas().GetSchemas()` and compare the pack against that runtime surface.

## Namespacing

Built-in Tempo definitions preserve their established component type names, such as `TmButton`, while the pack itself has `id = "tempo"` and `namespace = "tempo"`.

App-scoped components use `WireframeComponentScope`. A local type such as `Card` in app id `11111111-1111-1111-1111-111111111111` resolves to:

```text
app:11111111-1111-1111-1111-111111111111:Card
```

The canonical pattern is `app:{id}:{localType}`. Local type names must not contain `:`. A document or page that wants app components must include the app pack id in `targetPacks`, normally `app:{id}`. Built-ins remain visible.

## Component Contract

Each `components[]` item has:

- `type`: component type or local type.
- `displayName`: catalog label.
- `category`: catalog grouping.
- `icon`: optional icon name.
- `defaultSize`: `{ "width": number, "height": number }`.
- `minSize`, `maxSize`: optional size bounds.
- `sizePresets`: optional named size map.
- `resize`: `scale`, `nineSlice`, or `reflow`.
- `slice`: optional nine-slice insets.
- `props`: Tempo `PropDef` list.
- `contentSlots`: optional named slots.
- `impl`: optional framework implementation metadata.
- `render`: declarative render tree.
- `native`: trusted native renderer hook.

A component must define exactly one of `render` or `native`. `native{}` is allowed only for the built-in Tempo pack and resolves through `NativeRendererRegistry.TempoBuiltIn`. Uploaded PromptHelper app packs must not use `native{}`.

## Render Nodes

Render nodes are JSON objects with required `kind` and additional safe attributes. Reserved properties are `kind`, `when`, `text`, `value`, `children`, `props`, `prop`, `as`, and `node`.

Supported `kind` values are:

```text
group, rect, text, line, path, icon, spinner, image, svg, component, stack, row, grid, repeat, part
```

Meaning:

- `group`: SVG group with optional positioning/opacity.
- `rect`: rectangle using `x`, `y`, `w`, `h`, `fill`, `stroke`, `rx`, and related attributes.
- `text`: text with `text`/`value`/content slot, alignment, font sizing, and ellipsis.
- `line`: straight line.
- `path`: safe SVG path.
- `icon`: pack icon or registered Tempo icon.
- `spinner`: deterministic loading glyph.
- `image`: image placeholder or safe data URL image.
- `svg`: sanitized raw SVG fragment.
- `component`: nested stencil component rendered through the registry.
- `stack`, `row`, `grid`: layout containers.
- `repeat`: repeated child node from a count or prop.
- `part`: reusable pack part.

Raw SVG is sanitized. Unsafe elements and attributes, scripts, event handlers, `javascript:` URLs, and `foreignObject` are not part of the contract.

## Render Contract (Blazor hosts)

Container render nodes — `group`, `repeat`, `part` and `component` — are REAL SVG elements: the
renderer emits an actual `<g>` (via `WireframeSvg.OpenGroup`/`CloseGroup`, i.e. Blazor
`OpenElement`), so transforms, opacity and nesting reach the live DOM exactly as written. The
layout containers (`stack`, `row`, `grid`) place each child in a real `<g
transform=translate(…)>`, so per-child offsets reach the live DOM exactly as written. Never emit
a container as a raw markup string. The built-in `WireframeSvg` helpers are `internal` to the
library; a Blazor host copies the render pattern from the custom-provider sample in
[stencil-pack-authoring-guide.md](stencil-pack-authoring-guide.md) instead of calling them.

The same contract binds a host's own `RenderSvg` delegate (`WireframeComponentDef.RenderSvg`,
the `IWireframeComponentProvider` custom-provider hook):

- **Every `AddMarkupContent` frame MUST be a complete, standalone well-formed XML fragment.**
  Blazor parses each markup chunk into its OWN DOM template, so an opening `<g transform=…>`
  emitted in one frame and `</g>` in another never pairs up — the browser auto-closes the opening
  tag and the children become SIBLINGS, collapsing every transformed sketch in the editor, the
  toolbox preview and the client-side export. (The server-side string renderer only concatenates
  frames, so a split group stays invisible to every string-asserting test — only a per-frame parse
  catches it.)
- **To wrap children in a container, emit a real element** (`OpenElement` / `AddAttribute` /
  `CloseElement`), not markup strings. The library's built-in helpers that return markup strings
  (rect, text, …) are `internal` leaf fragments — hosts compose their own markup into one frame
  or place it inside an `OpenElement` (see the sample in
  [stencil-pack-authoring-guide.md](stencil-pack-authoring-guide.md)).
- **Guard.** `StencilPackRendererMarkupFrameInvariantTests` renders every built-in and
  JSON-provider definition and parses each emitted markup frame as `<root>{frame}</root>` with
  DTD processing prohibited; any orphan, unclosed or mismatched tag fails the suite.

## Binding Expressions

String values may be literals or safe expressions:

- `{prop}` for prop lookup.
- `{prop ?? "fallback"}` for fallback.
- `{condition ? "yes" : "no"}` for conditional values.
- comparisons and boolean operators such as `{count >= 3 && visible}`.
- numeric/string operations such as `{size.w - 12}` and `{"Hello " + label}`.
- `size.w`, `size.h`, and `repeat.index`.
- `$map{variant: primary=filled, danger=danger, *=default}`.
- `token("color.primary")` and `token("color.primary", "#2563eb")`.

`token()` is the only permitted function call. Any other function call is malformed and falls back to a literal value with `IsMalformed = true`; no function body is executed.

## Tokens And Themes

`tokens` provide pack defaults. `themes[name]` overrides selected tokens. The renderer uses the active token scope to resolve `token()` calls. Resolution is first-match-wins in this order: element override, document theme, pack theme, pack default, then the literal fallback supplied to `token(key, fallback)` or an empty string.

PromptHelper sets the active app/theme scope before catalog and preview rendering, so document-level `targetTheme` values participate before the pack theme/default layers.

## Wireframe Document Contract

`wireframe-document.schema.json` describes the current v2 paged document model:

- Root fields: `version`, `title`, `pages`, optional `activePageId`, `createdAt`, `modifiedAt`, `targetPacks`, and `targetTheme`.
- Page fields: `id`, `name`, `width`, `height`, `elements`, `connectors`, `layers`, optional page-level `targetPacks`, and optional page-level `targetTheme`.
- Element fields: `id`, `type`, `x`, `y`, `w`, `h`, `props`, `zIndex`, `groupId`, `isLocked`, `rotation`, `layerId`, and `lockedBy`.
- Connector fields: `id`, `fromId`, `toId`, `label`, `routing`, `waypoints`, arrows, stroke fields, and `zIndex`.

Page-level `targetPacks` and `targetTheme` override root values for validation/catalog filtering. Built-in Tempo components remain available; app-scoped components require the matching `app:{id}` target pack.

## Serialization

Stencil and wireframe JSON use camelCase field names, string enum values, indented output, and omitted null values. Consumers should ignore unknown fields for forward compatibility.

## Validation Invariants

The release invariant is:

- `tempo-stencil.schema.json` render node kinds match the `RenderNodeKind` runtime enum.
- The built-in pack has `id = "tempo"`, `namespace = "tempo"`, and `isBuiltIn = true`.
- Built-in pack component coverage is derived from `new BuiltInComponentSchemas().GetSchemas()`.
- App-scoped local types compile/register as `app:{id}:{localType}`.
- Binding expressions reject every function call except `token()`.
- `wireframe-document.schema.json` accepts current `WireframeSerializer` output.
- PromptHelper render paths use app stencil packs uploaded through PromptHelper, not CodeLibrary reflection.
