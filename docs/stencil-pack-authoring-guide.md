# Stencil Pack Authoring Guide

This guide is for authors creating `tempo-stencil` packs for Tempo.Blazor or PromptHelper app wireframes.

## Minimal Pack

```json
{
  "format": "tempo-stencil",
  "formatVersion": 1,
  "id": "sales-cards",
  "namespace": "app:sales",
  "target": { "framework": "agnostic", "library": "sales-ui", "version": "1.0.0" },
  "tokens": {
    "card.fill": "#ffffff",
    "card.border": "#cbd5e1",
    "text.default": "#0f172a"
  },
  "components": [
    {
      "type": "Card",
      "displayName": "Sales Card",
      "category": "Cards",
      "defaultSize": { "width": 220, "height": 120 },
      "props": [
        { "name": "title", "displayName": "Title", "type": "string", "default": "Pipeline" }
      ],
      "render": {
        "kind": "group",
        "children": [
          { "kind": "rect", "x": 0, "y": 0, "w": "size.w", "h": "size.h", "rx": 8, "fill": "token(\"card.fill\")", "stroke": "token(\"card.border\")" },
          { "kind": "text", "text": "{title ?? \"Pipeline\"}", "x": 16, "y": 24, "w": "size.w - 32", "fill": "token(\"text.default\")", "fontWeight": "600" }
        ]
      }
    }
  ]
}
```

When PromptHelper stores this for app id `11111111-1111-1111-1111-111111111111`, the local `Card` component is exposed to wireframes as `app:11111111-1111-1111-1111-111111111111:Card`. Add `app:11111111-1111-1111-1111-111111111111` to document or page `targetPacks` when the component should be available.

## Authoring Steps

1. Choose a pack `id` and `namespace`.
2. Define shared `tokens` first; use `themes` for dark or branded variants.
3. Add components with stable local `type` names.
4. Give every component a `displayName`, `category`, and `defaultSize`.
5. Use declarative `render` nodes for app packs. Do not use `native{}` in uploaded PromptHelper packs.
6. Validate the pack with the PromptHelper upload flow or `TempoStencilPackValidator`.
7. Render previews and review the generated SVG for legibility, clipping, and overlap.

## Render Node Quick Reference

Supported `kind` values:

```text
group, rect, text, line, path, icon, spinner, image, svg, component, stack, row, grid, repeat, part
```

Use `group` to position children, `rect` for surfaces, `text` for labels, `line` and `path` for strokes, `icon` for small symbols, `spinner` for loading states, `image` for safe data URL images or placeholders, `svg` for sanitized raw fragments, `component` for nesting registered stencil components, `stack`, `row`, and `grid` for layout, `repeat` for repeated rows/items, and `part` for reusable snippets.

## Binding Rules

Use `{prop}` for prop lookup and `token("name")` for token lookup. `token()` is the only allowed function. These are valid:

```text
{title ?? "Untitled"}
{isActive ? "#2563eb" : "#94a3b8"}
{size.w - 24}
$map{variant: primary=#2563eb, danger=#dc2626, *=#64748b}
token("card.fill", "#ffffff")
```

Avoid dynamic function syntax. Expressions such as `{eval("1+1")}` or `token(userInput)` are rejected and rendered as malformed literal fallbacks.

## Render Contract For Custom Blazor Components

A host that registers custom components through `IWireframeComponentProvider` (instead of a JSON
pack) implements `WireframeComponentDef.RenderSvg`. The render contract is load-bearing:

- **Every `AddMarkupContent` frame must be a complete, standalone well-formed XML fragment** —
  never an opening tag in one frame and its closing tag in another. Blazor parses each markup
  chunk into its OWN DOM template: a split `<g transform=…>` / `</g>` pair auto-closes in the
  browser, the children become siblings, and every transformed sketch collapses in the editor,
  the toolbox preview and the client-side export. The server-side string renderer concatenates
  frames, so string-asserting tests stay green either way — a per-frame parse is the only check
  that catches it (`StencilPackRendererMarkupFrameInvariantTests` guards every built-in and
  JSON-provider definition the same way).
- **Wrap children in a real element, not a markup string**: `b.OpenElement(1, "g")`,
  `b.AddAttribute(2, "transform", …)`, render the children, `b.CloseElement()`.
- Container render nodes of JSON packs (`group`, `repeat`, `part`, `component`) follow the same
  rule internally — they are real SVG `<g>` elements, so pack authors can rely on transforms and
  opacity nesting exactly as written. The layout containers (`stack` / `row` / `grid`) place each
  child in a real `<g transform=translate(…)>` of its own, so per-child offsets reach the live
  DOM exactly as written. The built-in `WireframeSvg` helpers are `internal` to the library —
  hosts copy the pattern from the `RenderHero` sample above instead of calling them.

```csharp
private static void RenderHero(WireframeElement el, RenderTreeBuilder b)
{
    // Leaf markup: ONE frame holding complete, well-formed fragments —
    // Blazor parses each AddMarkupContent chunk into its OWN DOM template,
    // so an opening tag and its closing tag must never be split across frames.
    // User text is HTML-encoded; doubles are formatted invariantly.
    var heading    = System.Net.WebUtility.HtmlEncode(el.GetString("heading", "Hero"));
    var subheading = System.Net.WebUtility.HtmlEncode(el.GetString("subheading", ""));
    b.AddMarkupContent(0,
        string.Create(CultureInfo.InvariantCulture, $"<rect width='{el.W}' height='{el.H}' rx='8' fill='#f0f9ff' stroke='#bae6fd'></rect>") +
        $"<text x='16' y='32' font-size='18' font-weight='600' fill='#374151'>{heading}</text>" +
        $"<text x='16' y='56' font-size='11' fill='#6b7280'>{subheading}</text>");

    // Container: a REAL element — children stay inside the group.
    b.OpenElement(1, "g");
    b.AddAttribute(2, "transform", string.Create(CultureInfo.InvariantCulture, $"translate(16,{el.H - 40})"));
    b.AddMarkupContent(3, "<rect width='120' height='28' rx='6' fill='#dbeafe'></rect>");
    b.CloseElement();
}
```

## Sizing And Layout

Use `size.w` and `size.h` for elements that should stretch with the placed wireframe element. Prefer stable dimensions for labels and icons. For dense controls, reserve enough `w`/`h` on text nodes and set `ellipsis` when long user text may appear.

Use `stack`, `row`, and `grid` when children should reflow. Use `resize: "nineSlice"` and `slice` only when a surface needs stable corners while scaling.

## Safety Checklist

- App packs use `render`, not `native{}`.
- Raw `svg` content contains only safe SVG, no scripts, no event attributes, no `foreignObject`, and no `javascript:` URLs.
- `image` uses safe data URLs or accepts the placeholder fallback.
- Text comes from props or literals and is rendered as text, not executable markup.
- Every app component resolves through `app:{id}:{localType}` in PromptHelper.
- Documents using app components include the app pack in `targetPacks`.

## PromptHelper Flow

PromptHelper stores app stencil packs per app. Upload validation checks format, required fields, target metadata, component shape, and compile safety. The app stencil provider builds both catalog schemas and render definitions for the active app scope and theme. The render path uses the stored app stencil pack provider plus Tempo built-ins; it does not discover app packs from CodeLibrary.

After upload, review component previews and any use-case wireframe previews. Check that text is readable, icons are visible, layout is not clipped, and app components do not fall back to dashed unknown-component boxes.
